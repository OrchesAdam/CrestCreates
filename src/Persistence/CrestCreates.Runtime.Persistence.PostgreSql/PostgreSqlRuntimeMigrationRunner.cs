using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Applies an immutable, checksummed migration catalog. Validation-only mode
/// executes no DDL and refuses a database which is not exactly compatible.
/// </summary>
public sealed class PostgreSqlRuntimeMigrationRunner
{
    private const string HistoryTable = "crest_runtime_schema_migrations";
    private readonly PostgreSqlRuntimePersistenceOptions _options;

    public PostgreSqlRuntimeMigrationRunner(PostgreSqlRuntimePersistenceOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task ApplyAsync(PostgreSqlRuntimeMigrationOptions migrationOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrationOptions);
        PostgreSqlRuntimePersistenceOptionsValidator.Validate(_options);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!migrationOptions.ApplyMigrations)
        {
            await ValidateOnlyAsync(connection, cancellationToken).ConfigureAwait(false);
            return;
        }

        var lockKey = $"crest-runtime-migrations:{_options.Schema}";
        await ExecuteAsync(connection, "select pg_advisory_lock(hashtext(@key));", [new("key", lockKey)], cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureBootstrapAsync(connection, cancellationToken).ConfigureAwait(false);
            await ValidateHistoryTableShapeAsync(connection, cancellationToken).ConfigureAwait(false);
            var applied = await ReadHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
            ValidateHistory(applied, allowPending: true);
            foreach (var migration in Catalog.Skip(applied.Count))
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, migration.Sql.Replace("{schema}", QuotedSchema(), StringComparison.Ordinal), [], cancellationToken, transaction).ConfigureAwait(false);
                await ExecuteAsync(connection,
                    $"insert into {Qualified(HistoryTable)} (version, name, checksum) values (@version, @name, @checksum);",
                    [new("version", migration.Version), new("name", migration.Name), new("checksum", migration.Checksum)],
                    cancellationToken,
                    transaction).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            var finalHistory = await ReadHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
            ValidateHistory(finalHistory, allowPending: false);
            await ValidateRequiredTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ExecuteAsync(connection, "select pg_advisory_unlock(hashtext(@key));", [new("key", lockKey)], CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ValidateOnlyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var exists = await ScalarAsync<bool>(connection,
            "select exists (select 1 from information_schema.schemata where schema_name=@schema) and exists (select 1 from information_schema.tables where table_schema=@schema and table_name=@history);",
            [new("schema", _options.Schema), new("history", HistoryTable)],
            cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new InvalidOperationException(
                "PostgreSQL runtime schema/history is missing; validation-only mode executes no DDL.");
        }

        await ValidateHistoryTableShapeAsync(connection, cancellationToken).ConfigureAwait(false);
        var history = await ReadHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateHistory(history, allowPending: false);
        await ValidateRequiredTablesAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureBootstrapAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, $"create schema if not exists {QuotedSchema()};", [], cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection,
            $"create table if not exists {Qualified(HistoryTable)} (version text primary key, name text not null, checksum text not null, applied_at timestamptz not null default clock_timestamp());",
            [], cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AppliedMigration>> ReadHistoryAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select version, name, checksum from {Qualified(HistoryTable)} order by version collate \"C\";", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new AppliedMigration(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private async Task ValidateHistoryTableShapeAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var columns = new Dictionary<string, (string Type, string Nullable)>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
            "select column_name, data_type, is_nullable from information_schema.columns where table_schema=@schema and table_name=@table order by ordinal_position;",
            connection))
        {
            command.Parameters.AddWithValue("schema", _options.Schema);
            command.Parameters.AddWithValue("table", HistoryTable);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                columns.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2)));
        }

        var expected = new Dictionary<string, (string Type, string Nullable)>(StringComparer.Ordinal)
        {
            ["version"] = ("text", "NO"),
            ["name"] = ("text", "NO"),
            ["checksum"] = ("text", "NO"),
            ["applied_at"] = ("timestamp with time zone", "NO")
        };
        if (columns.Count != expected.Count || expected.Any(pair => !columns.TryGetValue(pair.Key, out var actual) || actual != pair.Value))
            throw new InvalidOperationException("PostgreSQL runtime migration history table has an incompatible shape.");

        var primaryKeyColumns = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            select key_column.column_name
            from information_schema.table_constraints constraint_info
            join information_schema.key_column_usage key_column
              on key_column.constraint_name = constraint_info.constraint_name
             and key_column.table_schema = constraint_info.table_schema
             and key_column.table_name = constraint_info.table_name
            where constraint_info.table_schema=@schema
              and constraint_info.table_name=@table
              and constraint_info.constraint_type='PRIMARY KEY'
            order by key_column.ordinal_position;
            """,
            connection))
        {
            command.Parameters.AddWithValue("schema", _options.Schema);
            command.Parameters.AddWithValue("table", HistoryTable);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                primaryKeyColumns.Add(reader.GetString(0));
        }

        if (primaryKeyColumns.Count != 1 || !string.Equals(primaryKeyColumns[0], "version", StringComparison.Ordinal))
            throw new InvalidOperationException("PostgreSQL runtime migration history table must have a primary key on version.");
    }

    private void ValidateHistory(IReadOnlyList<AppliedMigration> applied, bool allowPending)
    {
        if (applied.Count > Catalog.Count)
            throw new InvalidOperationException("PostgreSQL runtime database contains a newer unknown migration.");
        for (var index = 0; index < applied.Count; index++)
        {
            var expected = Catalog[index];
            var actual = applied[index];
            if (!string.Equals(actual.Version, expected.Version, StringComparison.Ordinal)
                || !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal)
                || !string.Equals(actual.Checksum, expected.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"PostgreSQL runtime migration history is incompatible at '{actual.Version}'.");
            }
        }
        if (!allowPending && applied.Count != Catalog.Count)
            throw new InvalidOperationException("PostgreSQL runtime database has pending migrations in validation-only mode.");
    }

    private async Task ValidateRequiredTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var expected = new[]
        {
            "runtime_workflow_instances",
            "runtime_human_task_instances",
            "runtime_operation_receipts",
            "descriptor_snapshots",
            "descriptor_snapshot_entries",
            "runtime_audit_envelopes"
        };
        var count = await ScalarAsync<long>(connection,
            "select count(*) from information_schema.tables where table_schema=@schema and table_name = any(@tables);",
            [new("schema", _options.Schema), new("tables", expected)],
            cancellationToken).ConfigureAwait(false);
        if (count != expected.Length)
            throw new InvalidOperationException("PostgreSQL runtime schema has an unexpected or incomplete table shape.");

        foreach (var table in RuntimeSchemaManifest.Tables)
        {
            await ValidateTableColumnsAsync(connection, table, cancellationToken).ConfigureAwait(false);
            await ValidatePrimaryKeyAsync(connection, table, cancellationToken).ConfigureAwait(false);
            foreach (var check in table.RequiredChecks)
                await ValidateCheckAsync(connection, table.Name, check, cancellationToken).ConfigureAwait(false);
            foreach (var index in table.RequiredIndexes)
                await ValidateIndexAsync(connection, table.Name, index, cancellationToken).ConfigureAwait(false);
            foreach (var foreignKey in table.RequiredForeignKeys)
                await ValidateForeignKeyAsync(connection, table.Name, foreignKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ValidateTableColumnsAsync(NpgsqlConnection connection, RuntimeSchemaTable table, CancellationToken cancellationToken)
    {
        var actual = new Dictionary<string, (string Type, string Nullable)>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "select column_name, data_type, is_nullable from information_schema.columns where table_schema=@schema and table_name=@table;",
            connection);
        command.Parameters.AddWithValue("schema", _options.Schema);
        command.Parameters.AddWithValue("table", table.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            actual.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2)));

        if (actual.Count != table.Columns.Count || table.Columns.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidOperationException($"PostgreSQL runtime table '{table.Name}' has an incompatible column shape.");
    }

    private async Task ValidatePrimaryKeyAsync(NpgsqlConnection connection, RuntimeSchemaTable table, CancellationToken cancellationToken)
    {
        var actual = new List<string>();
        await using var command = new NpgsqlCommand("""
            select attribute.attname
            from pg_constraint constraint_info
            join pg_class relation on relation.oid = constraint_info.conrelid
            join pg_namespace schema_info on schema_info.oid = relation.relnamespace
            join unnest(constraint_info.conkey) with ordinality key_column(attnum, ordinal) on true
            join pg_attribute attribute on attribute.attrelid = relation.oid and attribute.attnum = key_column.attnum
            where schema_info.nspname=@schema and relation.relname=@table and constraint_info.contype='p'
            order by key_column.ordinal;
            """, connection);
        command.Parameters.AddWithValue("schema", _options.Schema);
        command.Parameters.AddWithValue("table", table.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) actual.Add(reader.GetString(0));
        if (!actual.SequenceEqual(table.PrimaryKey, StringComparer.Ordinal))
            throw new InvalidOperationException($"PostgreSQL runtime table '{table.Name}' has an incompatible primary key.");
    }

    private async Task ValidateCheckAsync(NpgsqlConnection connection, string table, RuntimeSchemaCheck expected, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select constraint_info.conname, pg_get_constraintdef(constraint_info.oid)
            from pg_constraint constraint_info
            join pg_class relation on relation.oid = constraint_info.conrelid
            join pg_namespace schema_info on schema_info.oid = relation.relnamespace
            where schema_info.nspname=@schema
              and relation.relname=@table
              and constraint_info.contype='c'
              and constraint_info.conname=@name;
            """, connection);
        command.Parameters.AddWithValue("schema", _options.Schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("name", expected.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || !string.Equals(NormalizeSql(reader.GetString(1)), NormalizeSql(expected.Definition), StringComparison.Ordinal))
            throw new InvalidOperationException($"PostgreSQL runtime table '{table}' is missing a required check constraint.");
    }

    private async Task ValidateIndexAsync(NpgsqlConnection connection, string table, RuntimeSchemaIndex expected, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select index_data.indisunique,
                   coalesce(pg_get_expr(index_data.indpred, index_data.indrelid), ''),
                   array(
                       select attribute.attname
                       from unnest(index_data.indkey) with ordinality index_key(attnum, ordinal)
                       join pg_attribute attribute on attribute.attrelid = table_relation.oid and attribute.attnum = index_key.attnum
                       order by index_key.ordinal)
            from pg_indexes index_info
            join pg_class index_relation on index_relation.relname = index_info.indexname
            join pg_namespace index_schema on index_schema.oid = index_relation.relnamespace and index_schema.nspname = index_info.schemaname
            join pg_index index_data on index_data.indexrelid = index_relation.oid
            join pg_class table_relation on table_relation.oid = index_data.indrelid
            where index_info.schemaname=@schema and index_info.tablename=@table and index_info.indexname=@name;
            """, connection);
        command.Parameters.AddWithValue("schema", _options.Schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("name", expected.Name);
        var expectedColumns = expected.Columns;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.GetBoolean(0) != expected.Unique
            || !string.Equals(NormalizeSql(reader.GetString(1)), NormalizeSql(expected.Predicate), StringComparison.Ordinal)
            || !reader.GetFieldValue<string[]>(2).SequenceEqual(expectedColumns, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"PostgreSQL runtime table '{table}' has an incompatible required index.");
        }
    }

    private async Task ValidateForeignKeyAsync(NpgsqlConnection connection, string table, RuntimeSchemaForeignKey expected, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select constraint_info.condeferrable,
                   constraint_info.condeferred,
                   referenced_schema.nspname,
                   referenced_relation.relname,
                   array(
                       select source_attribute.attname
                       from unnest(constraint_info.conkey) with ordinality source_key(attnum, ordinal)
                       join pg_attribute source_attribute on source_attribute.attrelid = relation.oid and source_attribute.attnum = source_key.attnum
                       order by source_key.ordinal),
                   array(
                       select referenced_attribute.attname
                       from unnest(constraint_info.confkey) with ordinality referenced_key(attnum, ordinal)
                       join pg_attribute referenced_attribute on referenced_attribute.attrelid = referenced_relation.oid and referenced_attribute.attnum = referenced_key.attnum
                       order by referenced_key.ordinal)
            from pg_constraint constraint_info
            join pg_class relation on relation.oid = constraint_info.conrelid
            join pg_namespace schema_info on schema_info.oid = relation.relnamespace
            join pg_class referenced_relation on referenced_relation.oid = constraint_info.confrelid
            join pg_namespace referenced_schema on referenced_schema.oid = referenced_relation.relnamespace
            where schema_info.nspname=@schema and relation.relname=@table and constraint_info.contype='f';
            """, connection);
        command.Parameters.AddWithValue("schema", _options.Schema);
        command.Parameters.AddWithValue("table", table);
        var expectedColumns = expected.Columns.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var expectedReferencedColumns = expected.ReferencedColumns.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetBoolean(0) == expected.Deferrable
                && reader.GetBoolean(1) == expected.InitiallyDeferred
                && string.Equals(reader.GetString(2), _options.Schema, StringComparison.Ordinal)
                && string.Equals(reader.GetString(3), expected.ReferencedTable, StringComparison.Ordinal)
                && reader.GetFieldValue<string[]>(4).SequenceEqual(expectedColumns, StringComparer.Ordinal)
                && reader.GetFieldValue<string[]>(5).SequenceEqual(expectedReferencedColumns, StringComparer.Ordinal))
            {
                return;
            }
        }
        throw new InvalidOperationException($"PostgreSQL runtime table '{table}' has an incompatible required foreign key.");
    }

    private static string NormalizeSql(string value)
    {
        var representation = value
            .Replace("::text", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        var canonical = CanonicalizeParentheses(representation);
        return string.Concat(canonical.Where(character => !char.IsWhiteSpace(character)));
    }

    private static string CanonicalizeParentheses(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '(')
            {
                result.Append(value[index]);
                continue;
            }

            var closingIndex = FindMatchingParenthesis(value, index);
            var inner = CanonicalizeParentheses(value[(index + 1)..closingIndex]);
            if (ContainsTopLevelBooleanOperator(inner))
                result.Append('(').Append(inner).Append(')');
            else
                result.Append(inner);
            index = closingIndex;
        }

        const string checkPrefix = "check";
        var canonical = result.ToString();
        if (!canonical.StartsWith(checkPrefix, StringComparison.Ordinal))
        {
            while (IsEntireParenthesizedExpression(canonical.Trim()))
                canonical = canonical.Trim()[1..^1].Trim();
            return canonical;
        }

        var expression = canonical[checkPrefix.Length..].Trim();
        while (IsEntireParenthesizedExpression(expression))
            expression = expression[1..^1].Trim();
        return checkPrefix + expression;
    }

    private static int FindMatchingParenthesis(string value, int openingIndex)
    {
        var depth = 0;
        for (var index = openingIndex; index < value.Length; index++)
        {
            if (value[index] == '(') depth++;
            if (value[index] != ')') continue;
            if (--depth == 0) return index;
        }
        throw new InvalidOperationException("PostgreSQL schema manifest contains an unbalanced expression.");
    }

    private static bool ContainsTopLevelBooleanOperator(string value)
    {
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '(') { depth++; continue; }
            if (value[index] == ')') { depth--; continue; }
            if (depth == 0
                && ((index == 0 || IsBooleanBoundary(value[index - 1]))
                    && (value.AsSpan(index).StartsWith("and", StringComparison.Ordinal)
                        || value.AsSpan(index).StartsWith("or", StringComparison.Ordinal))
                    && (index + (value.AsSpan(index).StartsWith("and", StringComparison.Ordinal) ? 3 : 2) == value.Length
                        || IsBooleanBoundary(value[index + (value.AsSpan(index).StartsWith("and", StringComparison.Ordinal) ? 3 : 2)]))))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsBooleanBoundary(char character)
        => char.IsWhiteSpace(character) || character is '(' or ')';

    private static bool IsEntireParenthesizedExpression(string value)
        => value.Length > 1 && value[0] == '(' && FindMatchingParenthesis(value, 0) == value.Length - 1;

    private string QuotedSchema() => $"\"{_options.Schema}\"";
    private string Qualified(string table) => $"{QuotedSchema()}.\"{table}\"";

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        return (T)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private sealed record RuntimeMigration(string Version, string Name, string Sql)
    {
        public string Checksum { get; } = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Sql))).ToLowerInvariant();
    }

    private sealed record AppliedMigration(string Version, string Name, string Checksum);
    private sealed record RuntimeSchemaCheck(string Name, string Definition);
    private sealed record RuntimeSchemaIndex(string Name, IReadOnlyList<string> Columns, string Predicate, bool Unique = true);
    private sealed record RuntimeSchemaForeignKey(
        string Columns,
        string ReferencedTable,
        string ReferencedColumns,
        bool Deferrable = true,
        bool InitiallyDeferred = true);
    private sealed record RuntimeSchemaTable(
        string Name,
        IReadOnlyDictionary<string, (string Type, string Nullable)> Columns,
        IReadOnlyList<string> PrimaryKey,
        IReadOnlyList<RuntimeSchemaCheck> RequiredChecks,
        IReadOnlyList<RuntimeSchemaIndex> RequiredIndexes,
        IReadOnlyList<RuntimeSchemaForeignKey> RequiredForeignKeys);

    private static class RuntimeSchemaManifest
    {
        private static readonly (string Type, string Nullable) Text = ("text", "NO");
        private static readonly (string Type, string Nullable) NullableText = ("text", "YES");
        private static readonly (string Type, string Nullable) BigInt = ("bigint", "NO");
        private static readonly (string Type, string Nullable) Integer = ("integer", "NO");
        private static readonly (string Type, string Nullable) Json = ("jsonb", "NO");
        private static readonly (string Type, string Nullable) Timestamp = ("timestamp with time zone", "NO");
        private static readonly (string Type, string Nullable) NullableTimestamp = ("timestamp with time zone", "YES");

        public static readonly IReadOnlyList<RuntimeSchemaTable> Tables =
        [
            new("runtime_workflow_instances", new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = Text, ["tenant_id"] = Text, ["instance_id"] = Text,
                ["revision"] = BigInt, ["status"] = Integer, ["workflow_pin_json"] = Json,
                ["waiting_instance_id"] = NullableText, ["suspension_operation_id"] = NullableText,
                ["state_json"] = Json, ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "instance_id"],
            [new("ck_runtime_workflow_revision", "check (revision > 0)"),
             new("ck_runtime_workflow_tenant_scope", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))")],
            [new("ux_runtime_workflow_waiting", ["tenant_scope_kind", "tenant_id", "waiting_instance_id"], "waiting_instance_id is not null")],
            [new("tenant_scope_kind, tenant_id, instance_id, waiting_instance_id", "runtime_human_task_instances", "tenant_scope_kind, tenant_id, workflow_instance_id, instance_id")]),
            new("runtime_human_task_instances", new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = Text, ["tenant_id"] = Text, ["instance_id"] = Text,
                ["revision"] = BigInt, ["status"] = Integer, ["human_task_pin_json"] = Json,
                ["workflow_instance_id"] = NullableText, ["workflow_step_id"] = NullableText,
                ["suspension_operation_id"] = NullableText, ["assignee_user_id"] = NullableText,
                ["state_json"] = Json, ["created_at"] = Timestamp, ["updated_at"] = Timestamp,
                ["completed_at"] = NullableTimestamp, ["cancelled_at"] = NullableTimestamp
            }, ["tenant_scope_kind", "tenant_id", "instance_id"],
            [new("ck_runtime_human_task_revision", "check (revision > 0)"),
             new("ck_runtime_human_task_tenant_scope", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
             new("ck_runtime_human_task_workflow_step_pair", "check ((workflow_instance_id is null and workflow_step_id is null) or (workflow_instance_id is not null and workflow_step_id is not null))"),
             new("ck_runtime_human_task_lifecycle", "check ((status = any (array[0, 1]) and completed_at is null and cancelled_at is null) or (status = any (array[2, 4]) and completed_at is not null and cancelled_at is null) or (status = 3 and completed_at is null and cancelled_at is not null))")],
            [new("ux_runtime_human_task_active_step", ["tenant_scope_kind", "tenant_id", "workflow_instance_id", "workflow_step_id"], "workflow_instance_id is not null and workflow_step_id is not null and completed_at is null and cancelled_at is null"),
             new("uq_runtime_human_task_workflow_instance", ["tenant_scope_kind", "tenant_id", "workflow_instance_id", "instance_id"], "")],
            [new("tenant_scope_kind, tenant_id, workflow_instance_id", "runtime_workflow_instances", "tenant_scope_kind, tenant_id, instance_id")]),
            new("runtime_operation_receipts", new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = Text, ["tenant_id"] = Text, ["operation_id"] = Text,
                ["workflow_instance_id"] = Text, ["human_task_instance_id"] = Text,
                ["workflow_from_revision"] = BigInt, ["workflow_to_revision"] = BigInt,
                ["integrity_json"] = Json, ["receipt_json"] = Json, ["committed_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "operation_id"],
            [new("ck_runtime_receipt_transition_revision", "check (workflow_to_revision = workflow_from_revision + 1)"),
             new("ck_runtime_receipt_tenant_scope", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))")], [],
            [new("tenant_scope_kind, tenant_id, workflow_instance_id", "runtime_workflow_instances", "tenant_scope_kind, tenant_id, instance_id"),
             new("tenant_scope_kind, tenant_id, workflow_instance_id, human_task_instance_id", "runtime_human_task_instances", "tenant_scope_kind, tenant_id, workflow_instance_id, instance_id")]),
            new("descriptor_snapshots", new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["snapshot_id"] = Text, ["content_hash"] = Text, ["snapshot_json"] = Json, ["created_at"] = Timestamp
            }, ["snapshot_id"], [], [], []),
            new("descriptor_snapshot_entries", new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["snapshot_id"] = Text, ["descriptor_namespace"] = Text, ["descriptor_id"] = Text,
                ["descriptor_version"] = Integer, ["contract_hash"] = Text, ["definition_hash"] = Text
            }, ["snapshot_id", "descriptor_namespace", "descriptor_id", "descriptor_version"], [], [],
            [new("snapshot_id", "descriptor_snapshots", "snapshot_id", Deferrable: false, InitiallyDeferred: false)]),
            new("runtime_audit_envelopes", new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["sink_id"] = Text, ["audit_id"] = Text, ["integrity_json"] = Json,
                ["envelope_json"] = Json, ["accepted_at"] = Timestamp
            }, ["sink_id", "audit_id"], [], [], [])
        ];
    }

    private static readonly IReadOnlyList<RuntimeMigration> Catalog =
    [
        new RuntimeMigration("V001", "runtime_instances", """
            create table {schema}.runtime_workflow_instances (
                tenant_scope_kind text not null,
                tenant_id text not null,
                instance_id text not null,
                revision bigint not null check (revision > 0),
                status integer not null,
                workflow_pin_json jsonb not null,
                waiting_instance_id text null,
                suspension_operation_id text null,
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, instance_id),
                check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))
            );
            create unique index ux_runtime_workflow_waiting on {schema}.runtime_workflow_instances (tenant_scope_kind, tenant_id, waiting_instance_id)
                where waiting_instance_id is not null;
            create table {schema}.runtime_human_task_instances (
                tenant_scope_kind text not null,
                tenant_id text not null,
                instance_id text not null,
                revision bigint not null check (revision > 0),
                status integer not null,
                human_task_pin_json jsonb not null,
                workflow_instance_id text null,
                workflow_step_id text null,
                suspension_operation_id text null,
                assignee_user_id text null,
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, instance_id),
                check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                check ((workflow_instance_id is null and workflow_step_id is null) or (workflow_instance_id is not null and workflow_step_id is not null))
            );
            create unique index ux_runtime_human_task_active_step on {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, workflow_step_id)
                where workflow_instance_id is not null;
            create table {schema}.runtime_operation_receipts (
                tenant_scope_kind text not null,
                tenant_id text not null,
                operation_id text not null,
                workflow_instance_id text not null,
                human_task_instance_id text not null,
                workflow_from_revision bigint not null,
                workflow_to_revision bigint not null,
                integrity_json jsonb not null,
                receipt_json jsonb not null,
                committed_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, operation_id),
                check (workflow_to_revision = workflow_from_revision + 1),
                check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))
            );
            """),
        new RuntimeMigration("V002", "descriptor_snapshot_evidence", """
            create table {schema}.descriptor_snapshots (
                snapshot_id text primary key,
                content_hash text not null,
                snapshot_json jsonb not null,
                created_at timestamptz not null default clock_timestamp()
            );
            create table {schema}.descriptor_snapshot_entries (
                snapshot_id text not null references {schema}.descriptor_snapshots(snapshot_id) on delete restrict,
                descriptor_namespace text not null,
                descriptor_id text not null,
                descriptor_version integer not null,
                contract_hash text not null,
                definition_hash text not null,
                primary key (snapshot_id, descriptor_namespace, descriptor_id, descriptor_version)
            );
            """),
        new RuntimeMigration("V003", "accountability_sink", """
            create table {schema}.runtime_audit_envelopes (
                sink_id text not null,
                audit_id text not null,
                integrity_json jsonb not null,
                envelope_json jsonb not null,
                accepted_at timestamptz not null default clock_timestamp(),
                primary key (sink_id, audit_id)
            );
            """),
        new RuntimeMigration("V004", "reciprocal_fks_and_active_step_lifecycle", """
            alter table {schema}.runtime_human_task_instances
                add column completed_at timestamptz null,
                add column cancelled_at timestamptz null;

            alter table {schema}.runtime_human_task_instances
                add constraint fk_human_task_workflow
                foreign key (tenant_scope_kind, tenant_id, workflow_instance_id)
                references {schema}.runtime_workflow_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_workflow_instances
                add constraint fk_workflow_waiting_task
                foreign key (tenant_scope_kind, tenant_id, waiting_instance_id)
                references {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_operation_receipts
                add constraint fk_receipt_workflow
                foreign key (tenant_scope_kind, tenant_id, workflow_instance_id)
                references {schema}.runtime_workflow_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_operation_receipts
                add constraint fk_receipt_human_task
                foreign key (tenant_scope_kind, tenant_id, human_task_instance_id)
                references {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            drop index if exists {schema}.ux_runtime_human_task_active_step;
            create unique index ux_runtime_human_task_active_step
                on {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, workflow_step_id)
                where workflow_instance_id is not null
                  and workflow_step_id is not null
                  and completed_at is null
                  and cancelled_at is null;
            """),
        new RuntimeMigration("V005", "reciprocal_correlation_and_task_lifecycle_invariants", """
            alter table {schema}.runtime_workflow_instances
                drop constraint fk_workflow_waiting_task;
            alter table {schema}.runtime_operation_receipts
                drop constraint fk_receipt_human_task;

            alter table {schema}.runtime_human_task_instances
                add constraint uq_runtime_human_task_workflow_instance
                unique (tenant_scope_kind, tenant_id, workflow_instance_id, instance_id);

            alter table {schema}.runtime_workflow_instances
                add constraint fk_workflow_waiting_task_reciprocal
                foreign key (tenant_scope_kind, tenant_id, instance_id, waiting_instance_id)
                references {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_operation_receipts
                add constraint fk_receipt_human_task_reciprocal
                foreign key (tenant_scope_kind, tenant_id, workflow_instance_id, human_task_instance_id)
                references {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_human_task_instances
                add constraint ck_runtime_human_task_lifecycle
                check (
                    (status in (0, 1) and completed_at is null and cancelled_at is null)
                    or (status in (2, 4) and completed_at is not null and cancelled_at is null)
                    or (status = 3 and completed_at is null and cancelled_at is not null)
                );
            """),
        new RuntimeMigration("V006", "named_schema_compatibility_checks", """
            alter table {schema}.runtime_human_task_instances
                drop constraint ck_runtime_human_task_lifecycle,
                add constraint ck_runtime_human_task_lifecycle
                check (
                    (status = any (array[0, 1]) and completed_at is null and cancelled_at is null)
                    or (status = any (array[2, 4]) and completed_at is not null and cancelled_at is null)
                    or (status = 3 and completed_at is null and cancelled_at is not null)
                );

            alter table {schema}.runtime_workflow_instances
                add constraint ck_runtime_workflow_revision check (revision > 0),
                add constraint ck_runtime_workflow_tenant_scope check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''));

            alter table {schema}.runtime_human_task_instances
                add constraint ck_runtime_human_task_revision check (revision > 0),
                add constraint ck_runtime_human_task_tenant_scope check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                add constraint ck_runtime_human_task_workflow_step_pair check ((workflow_instance_id is null and workflow_step_id is null) or (workflow_instance_id is not null and workflow_step_id is not null));

            alter table {schema}.runtime_operation_receipts
                add constraint ck_runtime_receipt_transition_revision check (workflow_to_revision = workflow_from_revision + 1),
                add constraint ck_runtime_receipt_tenant_scope check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''));
            """)
    ];
}
