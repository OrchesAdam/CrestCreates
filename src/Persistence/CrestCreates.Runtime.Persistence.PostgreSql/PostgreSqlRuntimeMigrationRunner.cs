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
                await PostgreSqlRuntimeTestHooks.NotifyBeforeMigrationAsync(migration.Version, cancellationToken).ConfigureAwait(false);
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
            "runtime_audit_envelopes",
            "agent_tool_pre_dispatch_checkpoints",
            "agent_tool_budget_reservations",
            "agent_tool_invocation_pre_dispatch",
            "agent_tool_governance_decisions",
            "agent_tool_governance_finalizations",
            "agent_tool_reconciliation_observations",
            "agent_tool_reconciliation_receipts",
            "control_plane_descriptor_drafts",
            "organization_units",
            "organization_positions",
            "organization_memberships",
            "organization_role_assignments",
            "data_permission_scope_rules"
            ,"runtime_outbox_messages"
            ,"runtime_workflow_continuation_acceptances"
            ,"organization_scope_generations"
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
            await ValidateChecksExactAsync(connection, table, cancellationToken).ConfigureAwait(false);
            await ValidateIndexesExactAsync(connection, table, cancellationToken).ConfigureAwait(false);
            await ValidateForeignKeysExactAsync(connection, table, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ValidateChecksExactAsync(
        NpgsqlConnection connection,
        RuntimeSchemaTable table,
        CancellationToken cancellationToken)
    {
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "select conname, pg_get_constraintdef(oid) from pg_constraint where conrelid = @relation::regclass and contype = 'c';",
            connection);
        command.Parameters.AddWithValue("relation", $"{_options.Schema}.{table.Name}");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            actual.Add(reader.GetString(0), NormalizeSql(reader.GetString(1)));

        var expected = table.RequiredChecks.ToDictionary(
            check => check.Name,
            check => NormalizeSql(check.Definition),
            StringComparer.Ordinal);
        if (actual.Count != expected.Count
            || actual.Any(pair => !expected.TryGetValue(pair.Key, out var definition)
                || !string.Equals(definition, pair.Value, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"PostgreSQL runtime table '{table.Name}' has an unexpected CHECK constraint set.");
        }
    }

    private async Task ValidateIndexesExactAsync(
        NpgsqlConnection connection,
        RuntimeSchemaTable table,
        CancellationToken cancellationToken)
    {
        var actualNames = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("""
            select index_info.indexname
            from pg_indexes index_info
            join pg_class index_relation on index_relation.relname = index_info.indexname
            join pg_namespace index_schema on index_schema.oid = index_relation.relnamespace
                and index_schema.nspname = index_info.schemaname
            where index_info.schemaname=@schema
              and index_info.tablename=@table
              and not exists (
                  select 1 from pg_constraint primary_key
                  where primary_key.conindid = index_relation.oid
                    and primary_key.contype = 'p')
            """, connection))
        {
            command.Parameters.AddWithValue("schema", _options.Schema);
            command.Parameters.AddWithValue("table", table.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                actualNames.Add(reader.GetString(0));
        }

        var expectedNames = table.RequiredIndexes.Select(index => index.Name).ToHashSet(StringComparer.Ordinal);
        if (!actualNames.SetEquals(expectedNames))
            throw new InvalidOperationException($"PostgreSQL runtime table '{table.Name}' has an unexpected non-primary index set.");

        foreach (var index in table.RequiredIndexes)
            await ValidateIndexAsync(connection, table.Name, index, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateForeignKeysExactAsync(
        NpgsqlConnection connection,
        RuntimeSchemaTable table,
        CancellationToken cancellationToken)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("""
            select constraint_info.condeferrable,
                   constraint_info.condeferred,
                   constraint_info.confdeltype,
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
        command.Parameters.AddWithValue("table", table.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            actual.Add(ForeignKeySignature(
                reader.GetBoolean(0),
                reader.GetBoolean(1),
                MapDeleteAction(reader.GetChar(2).ToString()),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<string[]>(5),
                reader.GetFieldValue<string[]>(6)));
        }

        var expected = table.RequiredForeignKeys
            .Select(foreignKey => ForeignKeySignature(
                foreignKey.Deferrable,
                foreignKey.InitiallyDeferred,
                foreignKey.DeleteAction,
                _options.Schema,
                foreignKey.ReferencedTable,
                foreignKey.Columns.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                foreignKey.ReferencedColumns.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
            throw new InvalidOperationException($"PostgreSQL runtime table '{table.Name}' has an unexpected foreign-key set.");
    }

    private static string ForeignKeySignature(
        bool deferrable,
        bool initiallyDeferred,
        string deleteAction,
        string schema,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> referencedColumns)
        => string.Join("|", deferrable, initiallyDeferred, deleteAction, schema, table,
            string.Join(",", columns), string.Join(",", referencedColumns));

    private async Task ValidateTableColumnsAsync(NpgsqlConnection connection, RuntimeSchemaTable table, CancellationToken cancellationToken)
    {
        var actual = new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "select column_name, data_type, is_nullable, collation_name from information_schema.columns where table_schema=@schema and table_name=@table;",
            connection);
        command.Parameters.AddWithValue("schema", _options.Schema);
        command.Parameters.AddWithValue("table", table.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var collation = reader.IsDBNull(3) ? null : reader.GetString(3);
            actual.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2), collation));
        }

        if (actual.Count != table.Columns.Count
            || table.Columns.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
        {
            throw new InvalidOperationException($"PostgreSQL runtime table '{table.Name}' has an incompatible column shape or collation.");
        }
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
                       order by index_key.ordinal),
                   array(
                       select case when index_key.collation_oid = 0 then '' else coll.collname end
                       from unnest(index_data.indcollation) with ordinality index_key(collation_oid, ordinal)
                       left join pg_collation coll on coll.oid = index_key.collation_oid
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
            || !reader.GetFieldValue<string[]>(2).SequenceEqual(expectedColumns, StringComparer.Ordinal)
            || (expected.KeyCollations is not null
                && !reader.GetFieldValue<string[]>(3).SequenceEqual(expected.KeyCollations, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException($"PostgreSQL runtime table '{table}' has an incompatible required index.");
        }
    }

    private async Task ValidateForeignKeyAsync(NpgsqlConnection connection, string table, RuntimeSchemaForeignKey expected, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select constraint_info.condeferrable,
                   constraint_info.condeferred,
                   constraint_info.confdeltype,
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
        var expectedDeleteAction = MapDeleteAction(expected.DeleteAction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetBoolean(0) == expected.Deferrable
                && reader.GetBoolean(1) == expected.InitiallyDeferred
                && MapDeleteAction(reader.GetChar(2).ToString()) == expectedDeleteAction
                && string.Equals(reader.GetString(3), _options.Schema, StringComparison.Ordinal)
                && string.Equals(reader.GetString(4), expected.ReferencedTable, StringComparison.Ordinal)
                && reader.GetFieldValue<string[]>(5).SequenceEqual(expectedColumns, StringComparer.Ordinal)
                && reader.GetFieldValue<string[]>(6).SequenceEqual(expectedReferencedColumns, StringComparer.Ordinal))
            {
                return;
            }
        }
        throw new InvalidOperationException($"PostgreSQL runtime table '{table}' has an incompatible required foreign key.");
    }

    private static string MapDeleteAction(string value)
        => value.ToUpperInvariant() switch
        {
            "A" => "NO ACTION",
            "R" => "RESTRICT",
            "C" => "CASCADE",
            "N" => "SET NULL",
            "D" => "SET DEFAULT",
            _ => value
        };

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
    private sealed record RuntimeSchemaIndex(
        string Name,
        IReadOnlyList<string> Columns,
        string Predicate,
        bool Unique = true,
        IReadOnlyList<string>? KeyCollations = null);
    private sealed record RuntimeSchemaForeignKey(
        string Columns,
        string ReferencedTable,
        string ReferencedColumns,
        bool Deferrable = true,
        bool InitiallyDeferred = true,
        string DeleteAction = "NO ACTION");
    private sealed record RuntimeSchemaTable(
        string Name,
        IReadOnlyDictionary<string, (string Type, string Nullable, string? Collation)> Columns,
        IReadOnlyList<string> PrimaryKey,
        IReadOnlyList<RuntimeSchemaCheck> RequiredChecks,
        IReadOnlyList<RuntimeSchemaIndex> RequiredIndexes,
        IReadOnlyList<RuntimeSchemaForeignKey> RequiredForeignKeys);

    private static class RuntimeSchemaManifest
    {
        private static readonly (string Type, string Nullable, string? Collation) Text = ("text", "NO", null);
        private static readonly (string Type, string Nullable, string? Collation) NullableText = ("text", "YES", null);
        private static readonly (string Type, string Nullable, string? Collation) BigInt = ("bigint", "NO", null);
        private static readonly (string Type, string Nullable, string? Collation) NullableBigInt = ("bigint", "YES", null);
        private static readonly (string Type, string Nullable, string? Collation) Integer = ("integer", "NO", null);
        private static readonly (string Type, string Nullable, string? Collation) Json = ("jsonb", "NO", null);
        private static readonly (string Type, string Nullable, string? Collation) NullableJson = ("jsonb", "YES", null);
        private static readonly (string Type, string Nullable, string? Collation) IntegerNullable = ("integer", "YES", null);
        private static readonly (string Type, string Nullable, string? Collation) Boolean = ("boolean", "NO", null);
        private static readonly (string Type, string Nullable, string? Collation) Timestamp = ("timestamp with time zone", "NO", null);
        private static readonly (string Type, string Nullable, string? Collation) NullableTimestamp = ("timestamp with time zone", "YES", null);
        private static readonly (string Type, string Nullable, string? Collation) TextC = ("text", "NO", "C");
        private static readonly (string Type, string Nullable, string? Collation) NullableTextC = ("text", "YES", "C");

        public static readonly IReadOnlyList<RuntimeSchemaTable> Tables =
        [
            new("runtime_workflow_instances", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = Text, ["tenant_id"] = Text, ["instance_id"] = Text,
                ["revision"] = BigInt, ["status"] = Integer, ["workflow_pin_json"] = Json,
                ["waiting_instance_id"] = NullableText, ["suspension_operation_id"] = NullableText,
                ["state_json"] = Json, ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "instance_id"],
             [new("ck_runtime_workflow_revision", "check (revision > 0)"),
              new("ck_runtime_workflow_tenant_scope", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
              new("runtime_workflow_instances_check", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
              new("runtime_workflow_instances_revision_check", "check (revision > 0)")],
            [new("ux_runtime_workflow_waiting", ["tenant_scope_kind", "tenant_id", "waiting_instance_id"], "waiting_instance_id is not null")],
            [new("tenant_scope_kind, tenant_id, instance_id, waiting_instance_id", "runtime_human_task_instances", "tenant_scope_kind, tenant_id, workflow_instance_id, instance_id", Deferrable: true, InitiallyDeferred: true, DeleteAction: "RESTRICT")]),
            new("runtime_human_task_instances", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = Text, ["tenant_id"] = Text, ["instance_id"] = Text,
                ["revision"] = BigInt, ["status"] = Integer, ["human_task_pin_json"] = Json,
                ["workflow_instance_id"] = NullableText, ["workflow_step_id"] = NullableText,
                ["suspension_operation_id"] = NullableText, ["assignee_user_id"] = NullableText,
                ["state_json"] = Json, ["created_at"] = Timestamp, ["updated_at"] = Timestamp,
                ["completed_at"] = NullableTimestamp, ["cancelled_at"] = NullableTimestamp,
                ["required_consumer_ids_json"] = Json
            }, ["tenant_scope_kind", "tenant_id", "instance_id"],
             [new("ck_runtime_human_task_revision", "check (revision > 0)"),
              new("ck_runtime_human_task_tenant_scope", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
              new("ck_runtime_human_task_workflow_step_pair", "check ((workflow_instance_id is null and workflow_step_id is null) or (workflow_instance_id is not null and workflow_step_id is not null))"),
              new("ck_runtime_human_task_lifecycle", "check ((status = any (array[0, 1]) and completed_at is null and cancelled_at is null) or (status = any (array[2, 4]) and completed_at is not null and cancelled_at is null) or (status = 3 and completed_at is null and cancelled_at is not null))"),
              new("runtime_human_task_instances_check", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
              new("runtime_human_task_instances_check1", "check ((workflow_instance_id is null and workflow_step_id is null) or (workflow_instance_id is not null and workflow_step_id is not null))"),
              new("runtime_human_task_instances_revision_check", "check (revision > 0)"),
              new("ck_runtime_human_task_required_consumers", "check (jsonb_typeof(required_consumer_ids_json) = 'array')"),
              new("ck_runtime_human_task_workflow_consumer", "check (workflow_instance_id is null or required_consumer_ids_json @> '[\"crest.workflow.humantask-continuation/v1\"]'::jsonb)")],
            [new("ux_runtime_human_task_active_step", ["tenant_scope_kind", "tenant_id", "workflow_instance_id", "workflow_step_id"], "workflow_instance_id is not null and workflow_step_id is not null and completed_at is null and cancelled_at is null"),
             new("uq_runtime_human_task_workflow_instance", ["tenant_scope_kind", "tenant_id", "workflow_instance_id", "instance_id"], "")],
            [new("tenant_scope_kind, tenant_id, workflow_instance_id", "runtime_workflow_instances", "tenant_scope_kind, tenant_id, instance_id", Deferrable: true, InitiallyDeferred: true, DeleteAction: "RESTRICT")]),
            new("runtime_operation_receipts", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = Text, ["tenant_id"] = Text, ["operation_id"] = Text,
                ["workflow_instance_id"] = Text, ["human_task_instance_id"] = Text,
                ["workflow_from_revision"] = BigInt, ["workflow_to_revision"] = BigInt,
                ["integrity_json"] = Json, ["receipt_json"] = Json, ["committed_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "operation_id"],
             [new("ck_runtime_receipt_transition_revision", "check (workflow_to_revision = workflow_from_revision + 1)"),
              new("ck_runtime_receipt_tenant_scope", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
              new("runtime_operation_receipts_check", "check (workflow_to_revision = workflow_from_revision + 1)"),
              new("runtime_operation_receipts_check1", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))")], [],
            [new("tenant_scope_kind, tenant_id, workflow_instance_id", "runtime_workflow_instances", "tenant_scope_kind, tenant_id, instance_id", Deferrable: true, InitiallyDeferred: true, DeleteAction: "RESTRICT"),
             new("tenant_scope_kind, tenant_id, workflow_instance_id, human_task_instance_id", "runtime_human_task_instances", "tenant_scope_kind, tenant_id, workflow_instance_id, instance_id", Deferrable: true, InitiallyDeferred: true, DeleteAction: "RESTRICT")]),
            new("descriptor_snapshots", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["snapshot_id"] = Text, ["content_hash"] = Text, ["snapshot_json"] = Json, ["created_at"] = Timestamp
            }, ["snapshot_id"], [], [], []),
            new("descriptor_snapshot_entries", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["snapshot_id"] = Text, ["descriptor_namespace"] = Text, ["descriptor_id"] = Text,
                ["descriptor_version"] = Integer, ["contract_hash"] = Text, ["definition_hash"] = Text
            }, ["snapshot_id", "descriptor_namespace", "descriptor_id", "descriptor_version"], [], [],
            [new("snapshot_id", "descriptor_snapshots", "snapshot_id", Deferrable: false, InitiallyDeferred: false, DeleteAction: "RESTRICT")]),
            new("runtime_audit_envelopes", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["sink_id"] = Text, ["audit_id"] = Text, ["integrity_json"] = Json,
                ["envelope_json"] = Json, ["accepted_at"] = Timestamp
            }, ["sink_id", "audit_id"], [], [], []),
            new("agent_tool_pre_dispatch_checkpoints", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = Text, ["audit_id"] = Text, ["attempt_id"] = Text,
                ["logical_invocation_key"] = Json, ["invocation_fingerprint"] = Text,
                ["arguments_hash"] = NullableText, ["arguments_evaluated"] = Boolean,
                ["call_origin"] = Integer, ["agent_roles_hash"] = NullableText,
                ["tool_contract_json"] = Json, ["capability_contract_json"] = Json,
                ["input_schema_contract_json"] = NullableJson, ["output_schema_contract_json"] = NullableJson,
                ["governance_json"] = Json, ["lease_json"] = Json,
                ["approval_json"] = Json, ["budget_reservation_json"] = Json,
                ["accepted_at"] = Timestamp, ["created_at"] = Timestamp
            }, ["tenant_id", "logical_invocation_key", "attempt_id"], [], [], []),
            new("agent_tool_budget_reservations", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = Text, ["reservation_id"] = Text, ["attempt_id"] = Text,
                ["logical_invocation_key"] = Json, ["invocation_fingerprint"] = Text,
                ["category"] = Text, ["cost_units"] = BigInt, ["max_calls_per_execution"] = IntegerNullable,
                ["state"] = Integer, ["created_at"] = Timestamp, ["updated_at"] = Timestamp,
                ["tool_contract_json"] = Json, ["capacity_key"] = Text
            }, ["tenant_id", "reservation_id"],
            [new("ck_agent_tool_budget_state_range", "check ((state >= 0) and (state <= 4))"),
             new("ck_agent_tool_budget_positive_costs", "check (cost_units > 0)"),
             new("ck_agent_tool_budget_maxcalls", "check ((max_calls_per_execution is null) or (max_calls_per_execution > 0))")],
            [new("ux_agent_tool_budget_attempt", ["tenant_id", "attempt_id"], ""),
             new("ix_agent_tool_budget_capacity", ["tenant_id", "capacity_key"], "", Unique: false)],
            []),
            new("agent_tool_invocation_pre_dispatch", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = Text, ["lease_id"] = Text, ["attempt_id"] = Text,
                ["logical_invocation_key"] = Json, ["invocation_fingerprint"] = Text,
                ["fencing_token"] = BigInt, ["acquired_at"] = Timestamp, ["expires_at"] = Timestamp,
                ["pre_dispatch_state"] = Integer, ["revision"] = BigInt,
                ["intent_json"] = NullableJson, ["bound_reservation_id"] = NullableText,
                ["bound_reservation_json"] = NullableJson,
                ["accepted_receipt_json"] = NullableJson, ["abandoned_receipt_json"] = NullableJson,
                ["last_reason_code"] = NullableText,
                ["dispatch_started_at"] = NullableTimestamp,
                ["completion_outcome_json"] = NullableJson, ["release_outcome_json"] = NullableJson,
                ["indeterminate_at"] = NullableTimestamp, ["indeterminate_reason"] = NullableText,
                ["completion_prepared_at"] = NullableTimestamp, ["release_prepared_at"] = NullableTimestamp,
                ["frozen_lease_json"] = NullableJson, ["reconciliation_claim_token"] = NullableText,
                ["reconciliation_claimed_at"] = NullableTimestamp,
                ["reconciliation_claimed_state"] = IntegerNullable,
                ["reconciliation_ownership_evidence"] = NullableText,
                ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_id", "lease_id"],
            [new("ck_agent_tool_pre_dispatch_state_range", "check ((pre_dispatch_state >= 0) and (pre_dispatch_state <= 11))"),
             new("ck_agent_tool_pre_dispatch_revision", "check (revision > 0)"),
             new("ck_agent_tool_pre_dispatch_fencing", "check (fencing_token > 0)"),
             new("ck_agent_tool_pre_dispatch_ready_shape", "check ((pre_dispatch_state <> 2) or (bound_reservation_id is not null))"),
             new("ck_agent_tool_pre_dispatch_accepted_shape", "check ((pre_dispatch_state <> 3) or (accepted_receipt_json is not null))"),
             new("ck_agent_tool_pre_dispatch_abandoned_shape", "check ((pre_dispatch_state <> 5) or (abandoned_receipt_json is not null))"),
             new("ck_agent_tool_pre_dispatch_release_pending_shape", "check ((pre_dispatch_state <> 6) or (release_outcome_json is not null))"),
             new("ck_agent_tool_pre_dispatch_completion_pending_shape", "check ((pre_dispatch_state <> 8) or (completion_outcome_json is not null))"),
             new("ck_agent_tool_pre_dispatch_reconciliation_shape", "check ((pre_dispatch_state <> 11) or (reconciliation_claim_token is not null))")],
            [new("ux_agent_tool_invocation_pre_dispatch_attempt", ["tenant_id", "attempt_id"], ""),
             new("ux_agent_tool_invocation_pre_dispatch_logical", ["tenant_id", "logical_invocation_key"], "pre_dispatch_state = any (array[0, 1, 2, 3, 4, 6, 8, 10, 11])"),
             new("ix_agent_tool_invocation_pre_dispatch_logical", ["tenant_id", "logical_invocation_key"], "", Unique: false)],
            []),
            new("agent_tool_governance_decisions", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = Text, ["audit_id"] = Text,
                ["logical_invocation_key"] = Json, ["attempt_id"] = Text,
                ["decision_state"] = Integer, ["decision_json"] = Json,
                ["created_at"] = Timestamp
            }, ["tenant_id", "audit_id"],
            [new("ck_agent_tool_decision_state_range", "check ((decision_state >= 0) and (decision_state <= 2))")],
            [new("ux_agent_tool_decision_identity", ["tenant_id", "logical_invocation_key", "attempt_id"], "")],
            []),
            new("agent_tool_governance_finalizations", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = Text, ["audit_id"] = Text,
                ["logical_invocation_key"] = Json, ["attempt_id"] = Text,
                ["attempt_state"] = Integer, ["finalization_json"] = Json,
                ["created_at"] = Timestamp
            }, ["tenant_id", "audit_id"],
            [new("ck_agent_tool_finalization_state_range", "check ((attempt_state >= 0) and (attempt_state <= 10))")],
            [], []),
            new("agent_tool_reconciliation_observations", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = Text, ["logical_invocation_key"] = Json, ["attempt_id"] = Text,
                ["revision"] = BigInt, ["status"] = Integer, ["reason_code"] = Text,
                ["observed_at"] = Timestamp, ["observation_json"] = NullableJson
             }, ["tenant_id", "logical_invocation_key", "attempt_id"],
             [new("agent_tool_reconciliation_observations_revision_check", "check (revision > 0)")], [], []),
            new("agent_tool_reconciliation_receipts", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = Text, ["logical_invocation_key"] = Json, ["attempt_id"] = Text,
                ["status"] = Integer, ["reason_code"] = Text,
                ["terminal_at"] = Timestamp, ["integrity_value"] = Text,
                ["receipt_json"] = Json, ["created_at"] = Timestamp
            }, ["tenant_id", "logical_invocation_key", "attempt_id"], [], [], []),
            new("agent_memory_conversations", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = TextC, ["conversation_id"] = TextC,
                ["revision"] = BigInt, ["state_contract_version"] = Integer,
                ["state_json"] = Json, ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_id", "conversation_id"],
            [new("ck_agent_memory_conversations_revision", "check (revision > 0)"),
             new("ck_agent_memory_conversations_contract_version", "check (state_contract_version = 1)")], [], []),
            new("agent_memory_tasks", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = TextC, ["task_id"] = TextC,
                ["revision"] = BigInt, ["state_contract_version"] = Integer,
                ["state_json"] = Json, ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_id", "task_id"],
            [new("ck_agent_memory_tasks_revision", "check (revision > 0)"),
             new("ck_agent_memory_tasks_contract_version", "check (state_contract_version = 1)")], [], []),
            new("agent_memory_compressed_contexts", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = TextC, ["context_id"] = TextC,
                ["revision"] = BigInt, ["state_contract_version"] = Integer,
                ["state_json"] = Json, ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_id", "context_id"],
            [new("ck_agent_memory_contexts_revision", "check (revision > 0)"),
             new("ck_agent_memory_contexts_contract_version", "check (state_contract_version = 1)")], [], []),
            new("agent_memory_compressed_blocks", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = TextC, ["block_id"] = TextC, ["context_id"] = TextC,
                ["ordinal"] = Integer, ["state_contract_version"] = Integer,
                ["block_json"] = Json
            }, ["tenant_id", "block_id"],
            [new("ck_agent_memory_compressed_blocks_ordinal_nonnegative", "check (ordinal >= 0)"),
             new("ck_agent_memory_compressed_blocks_contract_version", "check (state_contract_version = 1)")],
            [new("uq_agent_memory_blocks_context_ordinal", ["tenant_id", "context_id", "ordinal"], "")],
            [new("tenant_id, context_id", "agent_memory_compressed_contexts", "tenant_id, context_id", Deferrable: false, InitiallyDeferred: false, DeleteAction: "CASCADE")]),
            new("agent_memory_candidates", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = TextC, ["candidate_id"] = TextC,
                ["revision"] = BigInt, ["status"] = Integer, ["kind"] = Integer,
                ["canonical_content_hash"] = TextC, ["state_hash"] = TextC,
                ["state_contract_version"] = Integer, ["state_json"] = Json,
                ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_id", "candidate_id"],
            [new("ck_agent_memory_candidates_status", "check ((status >= 0) and (status <= 4))"),
             new("ck_agent_memory_candidates_kind", "check ((kind >= 0) and (kind <= 5))"),
             new("ck_agent_memory_candidates_revision", "check (revision > 0)"),
             new("ck_agent_memory_candidates_contract_version", "check (state_contract_version = 1)")],
            [new("ix_agent_memory_candidates_tenant_status", ["tenant_id", "status", "candidate_id"], "", Unique: false)], []),
            new("agent_memories", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = TextC, ["memory_id"] = TextC,
                ["revision"] = BigInt, ["status"] = Integer, ["kind"] = Integer, ["confidence"] = Integer,
                ["promoted_at"] = Timestamp,
                ["canonical_content_hash"] = TextC, ["state_hash"] = TextC,
                ["supersedes_memory_id"] = NullableTextC, ["superseded_by_memory_id"] = NullableTextC,
                ["state_contract_version"] = Integer, ["state_json"] = Json,
                ["created_at"] = Timestamp, ["updated_at"] = Timestamp
            }, ["tenant_id", "memory_id"],
            [new("ck_agent_memories_status", "check ((status >= 0) and (status <= 4))"),
             new("ck_agent_memories_kind", "check ((kind >= 0) and (kind <= 5))"),
             new("ck_agent_memories_confidence", "check ((confidence >= 0) and (confidence <= 3))"),
             new("ck_agent_memories_revision", "check (revision > 0)"),
             new("ck_agent_memories_contract_version", "check (state_contract_version = 1)"),
             new("ck_agent_memories_no_self_supersedes", "check ((supersedes_memory_id is null) or (supersedes_memory_id <> memory_id))"),
             new("ck_agent_memories_no_self_superseded_by", "check ((superseded_by_memory_id is null) or (superseded_by_memory_id <> memory_id))")],
            [new("uq_agent_memories_supersedes", ["tenant_id", "supersedes_memory_id"], "supersedes_memory_id is not null"),
             new("ix_agent_memories_tenant_status_kind", ["tenant_id", "status", "kind", "memory_id"], "", Unique: false)],
             [new("tenant_id, supersedes_memory_id", "agent_memories", "tenant_id, memory_id", Deferrable: true, InitiallyDeferred: true, DeleteAction: "NO ACTION"),
              new("tenant_id, superseded_by_memory_id", "agent_memories", "tenant_id, memory_id", Deferrable: true, InitiallyDeferred: true, DeleteAction: "NO ACTION")]),
            new("control_plane_descriptor_drafts", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_id"] = TextC, ["draft_id"] = TextC, ["payload_type"] = Integer,
                ["descriptor_kind"] = Integer, ["operation"] = Integer, ["author_kind"] = Integer,
                ["status"] = Integer, ["created_at_utc_ticks"] = BigInt, ["created_at"] = Timestamp,
                ["state_contract_version"] = Integer, ["state_json"] = Json, ["updated_at"] = Timestamp
            }, ["tenant_id", "draft_id"],
            [new("ck_cp_draft_payload_type", "check (payload_type = any (array[1,2,3,4,5,6]))"),
             new("ck_cp_draft_descriptor_kind", "check (descriptor_kind = any (array[0,1,2,3,4,5,6,7,8,9]))"),
             new("ck_cp_draft_operation", "check (operation = any (array[0,1,2,3]))"),
             new("ck_cp_draft_author_kind", "check (author_kind = any (array[0,1,2,3,4]))"),
             new("ck_cp_draft_status", "check (status = any (array[0,1,2,3,4]))"),
             new("ck_cp_draft_contract_version", "check (state_contract_version = 1)")],
            [new("ix_cp_drafts_created", ["tenant_id", "created_at_utc_ticks", "draft_id"], "", Unique: false, KeyCollations: ["C", "", "C"]),
             new("ix_cp_drafts_combined_filter", ["tenant_id", "descriptor_kind", "operation", "author_kind", "status", "created_at_utc_ticks", "draft_id"], "", Unique: false, KeyCollations: ["C", "", "", "", "", "", "C"])], []),
            new("organization_units", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["organization_unit_id"] = TextC,
                ["parent_id"] = NullableTextC, ["sort_order"] = Integer, ["is_active"] = Boolean,
                ["created_at_utc_ticks"] = BigInt, ["created_at"] = Timestamp,
                ["state_contract_version"] = Integer, ["state_json"] = Json, ["updated_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "organization_unit_id"],
            [new("ck_org_units_tenant_scope", "check ((tenant_scope_kind = 'global' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
             new("ck_org_units_contract_version", "check (state_contract_version = 1)")],
            [new("ix_org_units_explicit_list", ["tenant_scope_kind", "tenant_id", "sort_order", "organization_unit_id"], "", Unique: false, KeyCollations: ["C", "C", "", "C"]),
             new("ix_org_units_unfiltered_list", ["sort_order", "tenant_scope_kind", "tenant_id", "organization_unit_id"], "", Unique: false, KeyCollations: ["", "C", "C", "C"])], []),
            new("organization_positions", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["position_id"] = TextC,
                ["is_active"] = Boolean, ["created_at_utc_ticks"] = BigInt, ["created_at"] = Timestamp,
                ["state_contract_version"] = Integer, ["state_json"] = Json, ["updated_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "position_id"],
            [new("ck_org_positions_tenant_scope", "check ((tenant_scope_kind = 'global' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
             new("ck_org_positions_contract_version", "check (state_contract_version = 1)")], [], []),
            new("organization_memberships", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["membership_id"] = TextC,
                ["user_id"] = TextC, ["organization_unit_id"] = TextC, ["position_id"] = NullableTextC,
                ["is_primary"] = Boolean, ["is_active"] = Boolean, ["created_at_utc_ticks"] = BigInt,
                ["created_at"] = Timestamp, ["state_contract_version"] = Integer, ["state_json"] = Json,
                ["updated_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "membership_id"],
            [new("ck_org_memberships_tenant_scope", "check ((tenant_scope_kind = 'global' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
             new("ck_org_memberships_contract_version", "check (state_contract_version = 1)")],
            [new("ix_org_memberships_by_user", ["user_id", "tenant_scope_kind", "tenant_id", "created_at_utc_ticks", "membership_id"], "", Unique: false, KeyCollations: ["C", "C", "C", "", "C"]),
             new("ix_org_memberships_by_unit", ["organization_unit_id", "tenant_scope_kind", "tenant_id", "created_at_utc_ticks", "membership_id"], "", Unique: false, KeyCollations: ["C", "C", "C", "", "C"])], []),
            new("organization_role_assignments", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["assignment_id"] = TextC,
                ["user_id"] = TextC, ["role_id"] = TextC, ["organization_unit_id"] = NullableTextC,
                ["is_active"] = Boolean, ["created_at_utc_ticks"] = BigInt, ["created_at"] = Timestamp,
                ["state_contract_version"] = Integer, ["state_json"] = Json, ["updated_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "assignment_id"],
            [new("ck_org_roles_tenant_scope", "check ((tenant_scope_kind = 'global' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
             new("ck_org_roles_contract_version", "check (state_contract_version = 1)")],
            [new("ix_org_roles_by_user", ["user_id", "tenant_scope_kind", "tenant_id", "created_at_utc_ticks", "assignment_id"], "", Unique: false, KeyCollations: ["C", "C", "C", "", "C"])], []),
            new("data_permission_scope_rules", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["resource"] = TextC,
                ["action_match_kind"] = Integer, ["action_value"] = TextC,
                ["permission_match_kind"] = Integer, ["permission_value"] = TextC,
                ["scope_kind"] = Integer, ["updated_at"] = Timestamp
            }, ["tenant_scope_kind", "tenant_id", "resource", "action_match_kind", "action_value", "permission_match_kind", "permission_value"],
            [new("ck_data_permission_tenant_scope", "check ((tenant_scope_kind = 'global' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> '' and tenant_id <> '*'))"),
             new("ck_data_permission_action_match", "check ((action_match_kind = 0 and action_value <> '*') or (action_match_kind = 1 and action_value = ''))"),
             new("ck_data_permission_permission_match", "check ((permission_match_kind = 0 and permission_value <> '*') or (permission_match_kind = 1 and permission_value = ''))"),
             new("ck_data_permission_scope_kind", "check (scope_kind = any (array[0,1,2,3,4,5]))")], [], [])
            ,new("runtime_outbox_messages", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["message_id"] = TextC, ["contract_id"] = TextC, ["event_name"] = TextC, ["event_version"] = Integer,
                ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["correlation_id"] = NullableTextC, ["causation_id"] = NullableTextC,
                ["occurred_at"] = Timestamp, ["required_consumer_ids_json"] = Json, ["payload_utf8"] = ("bytea", "NO", null),
                ["integrity_json"] = Json, ["created_at"] = Timestamp, ["status"] = Integer, ["attempt_count"] = Integer,
                ["available_at"] = Timestamp, ["lease_owner_id"] = NullableTextC, ["fencing_token"] = BigInt, ["lease_expires_at"] = NullableTimestamp,
                ["last_failure_code"] = NullableTextC, ["last_failure_at"] = NullableTimestamp, ["delivered_at"] = NullableTimestamp,
                ["dead_lettered_at"] = NullableTimestamp, ["updated_at"] = Timestamp
                , ["terminal_lease_owner_id"] = NullableTextC, ["terminal_fencing_token"] = NullableBigInt, ["terminal_failure_code"] = NullableTextC
            }, ["message_id"],
            [new("ck_runtime_outbox_status", "check (status >= 0 and status <= 3)"),
             new("ck_runtime_outbox_event_version", "check (event_version > 0)"),
             new("ck_runtime_outbox_attempt", "check (attempt_count >= 0)"),
             new("ck_runtime_outbox_fencing_token", "check (fencing_token >= 0)"),
             new("ck_runtime_outbox_scope", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
             new("ck_runtime_outbox_required_consumers", "check (jsonb_typeof(required_consumer_ids_json) = 'array')"),
             new("ck_runtime_outbox_payload", "check (octet_length(payload_utf8) > 0)"),
             new("ck_runtime_outbox_pending_state", "check (status <> 0 or (lease_owner_id is null and lease_expires_at is null and delivered_at is null and dead_lettered_at is null))"),
             new("ck_runtime_outbox_leased_state", "check (status <> 1 or (lease_owner_id is not null and lease_expires_at is not null and delivered_at is null and dead_lettered_at is null))"),
             new("ck_runtime_outbox_delivered_state", "check (status <> 2 or (delivered_at is not null and dead_lettered_at is null and lease_owner_id is null and lease_expires_at is null))"),
             new("ck_runtime_outbox_dead_letter_state", "check (status <> 3 or (dead_lettered_at is not null and delivered_at is null and lease_owner_id is null and lease_expires_at is null))"),
             new("ck_runtime_outbox_terminal_fence", "check ((status < 2 and terminal_lease_owner_id is null and terminal_fencing_token is null) or (status >= 2 and terminal_lease_owner_id is not null and terminal_fencing_token is not null))")],
            [new("ix_runtime_outbox_claim", ["status", "available_at", "lease_expires_at", "occurred_at", "message_id"], "status = any (array[0, 1])", Unique: false)], [])
            ,new("runtime_workflow_continuation_acceptances", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
            {
                ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["completion_event_id"] = TextC,
                ["human_task_instance_id"] = TextC, ["workflow_instance_id"] = TextC, ["outcome"] = TextC,
                ["result_json"] = NullableJson, ["workflow_from_revision"] = BigInt, ["workflow_to_revision"] = BigInt,
                ["integrity_json"] = Json, ["receipt_json"] = Json, ["accepted_at"] = Timestamp
            }, ["completion_event_id"],
            [new("ck_runtime_continuation_acceptance_revision", "check (workflow_to_revision = workflow_from_revision + 1)"),
             new("ck_runtime_continuation_acceptance_tenant", "check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))")],
             [new("uq_runtime_continuation_acceptance_task", ["tenant_scope_kind", "tenant_id", "human_task_instance_id"], "", Unique: true)],
             [new("tenant_scope_kind, tenant_id, workflow_instance_id", "runtime_workflow_instances", "tenant_scope_kind, tenant_id, instance_id", DeleteAction: "RESTRICT"),
              new("tenant_scope_kind, tenant_id, workflow_instance_id, human_task_instance_id", "runtime_human_task_instances", "tenant_scope_kind, tenant_id, workflow_instance_id, instance_id", DeleteAction: "RESTRICT")])
             ,new("organization_scope_generations", new Dictionary<string, (string Type, string Nullable, string? Collation)>(StringComparer.Ordinal)
             {
                 ["tenant_scope_kind"] = TextC, ["tenant_id"] = TextC, ["generation"] = BigInt, ["updated_at"] = Timestamp
             }, ["tenant_scope_kind", "tenant_id"],
             [new("ck_org_scope_generation_tenant_scope", "check ((tenant_scope_kind = 'global' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))"),
              new("ck_org_scope_generation_value", "check (generation >= 1)")], [], [])
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
            """),
        new RuntimeMigration("V007", "agent_tool_pre_dispatch_reconciliation", """
            create table {schema}.agent_tool_pre_dispatch_checkpoints (
                tenant_id text not null,
                audit_id text not null,
                logical_invocation_key jsonb not null,
                attempt_id text not null,
                invocation_fingerprint text not null,
                arguments_hash text null,
                arguments_evaluated boolean not null,
                call_origin integer not null,
                agent_roles_hash text null,
                tool_contract_json jsonb not null,
                capability_contract_json jsonb not null,
                input_schema_contract_json jsonb null,
                output_schema_contract_json jsonb null,
                governance_json jsonb not null,
                lease_json jsonb not null,
                approval_json jsonb not null,
                budget_reservation_json jsonb not null,
                accepted_at timestamptz not null,
                created_at timestamptz not null default clock_timestamp(),
                primary key (tenant_id, logical_invocation_key, attempt_id)
            );

            create table {schema}.agent_tool_budget_reservations (
                tenant_id text not null,
                reservation_id text not null,
                attempt_id text not null,
                logical_invocation_key jsonb not null,
                invocation_fingerprint text not null,
                category text not null,
                cost_units bigint not null,
                max_calls_per_execution integer null,
                state integer not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_id, reservation_id)
            );
            create unique index ux_agent_tool_budget_attempt on {schema}.agent_tool_budget_reservations (tenant_id, attempt_id);

            create sequence {schema}.agent_tool_fencing_token_seq as bigint;

            create table {schema}.agent_tool_invocation_pre_dispatch (
                tenant_id text not null,
                lease_id text not null,
                attempt_id text not null,
                logical_invocation_key jsonb not null,
                invocation_fingerprint text not null,
                fencing_token bigint not null,
                acquired_at timestamptz not null,
                expires_at timestamptz not null,
                pre_dispatch_state integer not null default 0,
                revision bigint not null default 1,
                intent_json jsonb null,
                bound_reservation_id text null,
                bound_reservation_json jsonb null,
                accepted_receipt_json jsonb null,
                abandoned_receipt_json jsonb null,
                last_reason_code text null,
                dispatch_started_at timestamptz null,
                completion_outcome_json jsonb null,
                release_outcome_json jsonb null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_id, lease_id)
            );
            create unique index ux_agent_tool_invocation_pre_dispatch_attempt on {schema}.agent_tool_invocation_pre_dispatch (tenant_id, attempt_id);
            create unique index ux_agent_tool_invocation_pre_dispatch_logical on {schema}.agent_tool_invocation_pre_dispatch (tenant_id, logical_invocation_key) where pre_dispatch_state in (0, 1, 2, 3, 4, 6, 8, 10);
            create index ix_agent_tool_invocation_pre_dispatch_logical on {schema}.agent_tool_invocation_pre_dispatch (tenant_id, logical_invocation_key);

            create table {schema}.agent_tool_governance_decisions (
                tenant_id text not null,
                audit_id text not null,
                logical_invocation_key jsonb not null,
                attempt_id text not null,
                decision_state integer not null,
                decision_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                primary key (tenant_id, audit_id)
            );

            create table {schema}.agent_tool_governance_finalizations (
                tenant_id text not null,
                audit_id text not null,
                logical_invocation_key jsonb not null,
                attempt_id text not null,
                attempt_state integer not null,
                finalization_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                primary key (tenant_id, audit_id)
            );

            create table {schema}.agent_tool_reconciliation_observations (
                tenant_id text not null,
                logical_invocation_key jsonb not null,
                attempt_id text not null,
                revision bigint not null check (revision > 0),
                status integer not null,
                reason_code text not null,
                observed_at timestamptz not null default clock_timestamp(),
                observation_json jsonb null,
                primary key (tenant_id, logical_invocation_key, attempt_id)
            );

            create table {schema}.agent_tool_reconciliation_receipts (
                tenant_id text not null,
                logical_invocation_key jsonb not null,
                attempt_id text not null,
                status integer not null,
                reason_code text not null,
                terminal_at timestamptz not null,
                integrity_value text not null,
                receipt_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                primary key (tenant_id, logical_invocation_key, attempt_id)
            );
            """)
        , new RuntimeMigration("V008", "agent_tool_pre_dispatch_durable_semantics", """
            -- Phase 8f budget semantics: materialize the tool contract and capacity key
            -- so Reserve can enforce logical-invocation conflicts and MaxCallsPerExecution.
            alter table {schema}.agent_tool_budget_reservations
                add column tool_contract_json jsonb not null default '{}'::jsonb,
                add column capacity_key text not null default '';
            create index ix_agent_tool_budget_capacity on {schema}.agent_tool_budget_reservations (tenant_id, capacity_key);

            -- Indeterminate is a logical marker; the underlying Pending/Ready/Accepted
            -- recovery substate is preserved. Prepared-at timestamps support full
            -- completion/release receipt replay.
            alter table {schema}.agent_tool_invocation_pre_dispatch
                add column indeterminate_at timestamptz null,
                add column indeterminate_reason text null,
                add column completion_prepared_at timestamptz null,
                add column release_prepared_at timestamptz null;

            -- State-shape invariants.
            alter table {schema}.agent_tool_invocation_pre_dispatch
                add constraint ck_agent_tool_pre_dispatch_state_range check (pre_dispatch_state >= 0 and pre_dispatch_state <= 10),
                add constraint ck_agent_tool_pre_dispatch_revision check (revision > 0),
                add constraint ck_agent_tool_pre_dispatch_fencing check (fencing_token > 0),
                add constraint ck_agent_tool_pre_dispatch_ready_shape check (pre_dispatch_state <> 2 or bound_reservation_id is not null),
                add constraint ck_agent_tool_pre_dispatch_accepted_shape check (pre_dispatch_state <> 3 or accepted_receipt_json is not null),
                add constraint ck_agent_tool_pre_dispatch_abandoned_shape check (pre_dispatch_state <> 5 or abandoned_receipt_json is not null),
                add constraint ck_agent_tool_pre_dispatch_release_pending_shape check (pre_dispatch_state <> 6 or release_outcome_json is not null),
                add constraint ck_agent_tool_pre_dispatch_completion_pending_shape check (pre_dispatch_state <> 8 or completion_outcome_json is not null);

            alter table {schema}.agent_tool_budget_reservations
                add constraint ck_agent_tool_budget_state_range check (state >= 0 and state <= 4),
                add constraint ck_agent_tool_budget_positive_costs check (cost_units > 0),
                add constraint ck_agent_tool_budget_maxcalls check (max_calls_per_execution is null or max_calls_per_execution > 0);

            alter table {schema}.agent_tool_governance_decisions
                add constraint ck_agent_tool_decision_state_range check (decision_state >= 0 and decision_state <= 2);

            alter table {schema}.agent_tool_governance_finalizations
                add constraint ck_agent_tool_finalization_state_range check (attempt_state >= 0 and attempt_state <= 10);

            -- Stable decision identity: one decision per (tenant, logical invocation, attempt).
            create unique index ux_agent_tool_decision_identity
                on {schema}.agent_tool_governance_decisions (tenant_id, logical_invocation_key, attempt_id);
            """)
        , new RuntimeMigration("V009", "agent_tool_pre_dispatch_reconciliation_ownership", """
            -- Gate-owned reconciliation claim (P0: ownership fence). A claimed
            -- Attempt moves to state 11 (ReconciliationPending) with an immutable
            -- claim token; the live lease/fencing evidence is frozen in
            -- frozen_lease_json so governance finalization can still reference it.
            alter table {schema}.agent_tool_invocation_pre_dispatch
                add column frozen_lease_json jsonb null,
                add column reconciliation_claim_token text null,
                add column reconciliation_claimed_at timestamptz null,
                add column reconciliation_claimed_state integer null,
                add column reconciliation_ownership_evidence text null;

            alter table {schema}.agent_tool_invocation_pre_dispatch
                drop constraint ck_agent_tool_pre_dispatch_state_range;
            alter table {schema}.agent_tool_invocation_pre_dispatch
                add constraint ck_agent_tool_pre_dispatch_state_range check (pre_dispatch_state >= 0 and pre_dispatch_state <= 11),
                add constraint ck_agent_tool_pre_dispatch_reconciliation_shape check (pre_dispatch_state <> 11 or reconciliation_claim_token is not null);

            -- A claimed Attempt is still a logical-invocation fence: extend the
            -- partial unique index so Acquire cannot create a parallel Attempt.
            drop index {schema}.ux_agent_tool_invocation_pre_dispatch_logical;
            create unique index ux_agent_tool_invocation_pre_dispatch_logical
                on {schema}.agent_tool_invocation_pre_dispatch (tenant_id, logical_invocation_key)
                where pre_dispatch_state in (0, 1, 2, 3, 4, 6, 8, 10, 11);
            """),
        new RuntimeMigration("V010", "agent_memory_durable_store", """
            create table {schema}.agent_memory_conversations (
                tenant_id text collate "C" not null,
                conversation_id text collate "C" not null,
                revision bigint not null constraint ck_agent_memory_conversations_revision check (revision > 0),
                state_contract_version integer not null default 1 constraint ck_agent_memory_conversations_contract_version check (state_contract_version = 1),
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                constraint pk_agent_memory_conversations primary key (tenant_id, conversation_id)
            );

            create table {schema}.agent_memory_tasks (
                tenant_id text collate "C" not null,
                task_id text collate "C" not null,
                revision bigint not null constraint ck_agent_memory_tasks_revision check (revision > 0),
                state_contract_version integer not null default 1 constraint ck_agent_memory_tasks_contract_version check (state_contract_version = 1),
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                constraint pk_agent_memory_tasks primary key (tenant_id, task_id)
            );

            create table {schema}.agent_memory_compressed_contexts (
                tenant_id text collate "C" not null,
                context_id text collate "C" not null,
                revision bigint not null constraint ck_agent_memory_contexts_revision check (revision > 0),
                state_contract_version integer not null default 1 constraint ck_agent_memory_contexts_contract_version check (state_contract_version = 1),
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                constraint pk_agent_memory_compressed_contexts primary key (tenant_id, context_id)
            );

            create table {schema}.agent_memory_compressed_blocks (
                tenant_id text collate "C" not null,
                block_id text collate "C" not null,
                context_id text collate "C" not null,
                ordinal integer not null constraint ck_agent_memory_compressed_blocks_ordinal_nonnegative check (ordinal >= 0),
                state_contract_version integer not null default 1 constraint ck_agent_memory_compressed_blocks_contract_version check (state_contract_version = 1),
                block_json jsonb not null,
                constraint pk_agent_memory_compressed_blocks primary key (tenant_id, block_id),
                constraint uq_agent_memory_blocks_context_ordinal unique (tenant_id, context_id, ordinal),
                constraint fk_agent_memory_blocks_context
                    foreign key (tenant_id, context_id)
                    references {schema}.agent_memory_compressed_contexts (tenant_id, context_id)
                    on delete cascade
            );

            create table {schema}.agent_memory_candidates (
                tenant_id text collate "C" not null,
                candidate_id text collate "C" not null,
                revision bigint not null constraint ck_agent_memory_candidates_revision check (revision > 0),
                status integer not null constraint ck_agent_memory_candidates_status check (status between 0 and 4),
                kind integer not null constraint ck_agent_memory_candidates_kind check (kind between 0 and 5),
                canonical_content_hash text collate "C" not null,
                state_hash text collate "C" not null,
                state_contract_version integer not null default 1 constraint ck_agent_memory_candidates_contract_version check (state_contract_version = 1),
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                constraint pk_agent_memory_candidates primary key (tenant_id, candidate_id)
            );

            create table {schema}.agent_memories (
                tenant_id text collate "C" not null,
                memory_id text collate "C" not null,
                revision bigint not null constraint ck_agent_memories_revision check (revision > 0),
                status integer not null constraint ck_agent_memories_status check (status between 0 and 4),
                kind integer not null constraint ck_agent_memories_kind check (kind between 0 and 5),
                confidence integer not null constraint ck_agent_memories_confidence check (confidence between 0 and 3),
                promoted_at timestamptz not null,
                canonical_content_hash text collate "C" not null,
                state_hash text collate "C" not null,
                supersedes_memory_id text collate "C" null,
                superseded_by_memory_id text collate "C" null,
                state_contract_version integer not null default 1 constraint ck_agent_memories_contract_version check (state_contract_version = 1),
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                constraint pk_agent_memories primary key (tenant_id, memory_id),
                constraint ck_agent_memories_no_self_supersedes check (supersedes_memory_id is null or supersedes_memory_id <> memory_id),
                constraint ck_agent_memories_no_self_superseded_by check (superseded_by_memory_id is null or superseded_by_memory_id <> memory_id),
                constraint fk_agent_memories_supersedes
                    foreign key (tenant_id, supersedes_memory_id)
                    references {schema}.agent_memories (tenant_id, memory_id)
                    on delete no action
                    deferrable initially deferred,
                constraint fk_agent_memories_superseded_by
                    foreign key (tenant_id, superseded_by_memory_id)
                    references {schema}.agent_memories (tenant_id, memory_id)
                    on delete no action
                    deferrable initially deferred
            );
            create unique index uq_agent_memories_supersedes
                on {schema}.agent_memories (tenant_id, supersedes_memory_id)
                where supersedes_memory_id is not null;
            create index ix_agent_memories_tenant_status_kind
                on {schema}.agent_memories (tenant_id, status, kind, memory_id);
            create index ix_agent_memory_candidates_tenant_status
                on {schema}.agent_memory_candidates (tenant_id, status, candidate_id);
            """),
        new RuntimeMigration("V011", "control_plane_reference_data_stores", """
            create table {schema}.control_plane_descriptor_drafts (
                tenant_id text collate "C" not null,
                draft_id text collate "C" not null,
                payload_type integer not null,
                descriptor_kind integer not null,
                operation integer not null,
                author_kind integer not null,
                status integer not null,
                created_at_utc_ticks bigint not null,
                created_at timestamptz not null,
                state_contract_version integer not null,
                state_json jsonb not null,
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_id, draft_id),
                constraint ck_cp_draft_payload_type check (payload_type = any (array[1,2,3,4,5,6])),
                constraint ck_cp_draft_descriptor_kind check (descriptor_kind = any (array[0,1,2,3,4,5,6,7,8,9])),
                constraint ck_cp_draft_operation check (operation = any (array[0,1,2,3])),
                constraint ck_cp_draft_author_kind check (author_kind = any (array[0,1,2,3,4])),
                constraint ck_cp_draft_status check (status = any (array[0,1,2,3,4])),
                constraint ck_cp_draft_contract_version check (state_contract_version = 1)
            );
            create index ix_cp_drafts_created on {schema}.control_plane_descriptor_drafts (tenant_id, created_at_utc_ticks, draft_id);
            create index ix_cp_drafts_combined_filter on {schema}.control_plane_descriptor_drafts (tenant_id, descriptor_kind, operation, author_kind, status, created_at_utc_ticks, draft_id);

            create table {schema}.organization_units (
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                organization_unit_id text collate "C" not null,
                parent_id text collate "C" null,
                sort_order integer not null,
                is_active boolean not null,
                created_at_utc_ticks bigint not null,
                created_at timestamptz not null,
                state_contract_version integer not null,
                state_json jsonb not null,
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, organization_unit_id),
                constraint ck_org_units_tenant_scope check (
                    (tenant_scope_kind = 'global' and tenant_id = '')
                    or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                constraint ck_org_units_contract_version check (state_contract_version = 1)
            );
            create index ix_org_units_explicit_list on {schema}.organization_units (tenant_scope_kind, tenant_id, sort_order, organization_unit_id);
            create index ix_org_units_unfiltered_list on {schema}.organization_units (sort_order, tenant_scope_kind, tenant_id, organization_unit_id);

            create table {schema}.organization_positions (
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                position_id text collate "C" not null,
                is_active boolean not null,
                created_at_utc_ticks bigint not null,
                created_at timestamptz not null,
                state_contract_version integer not null,
                state_json jsonb not null,
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, position_id),
                constraint ck_org_positions_tenant_scope check (
                    (tenant_scope_kind = 'global' and tenant_id = '')
                    or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                constraint ck_org_positions_contract_version check (state_contract_version = 1)
            );

            create table {schema}.organization_memberships (
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                membership_id text collate "C" not null,
                user_id text collate "C" not null,
                organization_unit_id text collate "C" not null,
                position_id text collate "C" null,
                is_primary boolean not null,
                is_active boolean not null,
                created_at_utc_ticks bigint not null,
                created_at timestamptz not null,
                state_contract_version integer not null,
                state_json jsonb not null,
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, membership_id),
                constraint ck_org_memberships_tenant_scope check (
                    (tenant_scope_kind = 'global' and tenant_id = '')
                    or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                constraint ck_org_memberships_contract_version check (state_contract_version = 1)
            );
            create index ix_org_memberships_by_user on {schema}.organization_memberships (user_id, tenant_scope_kind, tenant_id, created_at_utc_ticks, membership_id);
            create index ix_org_memberships_by_unit on {schema}.organization_memberships (organization_unit_id, tenant_scope_kind, tenant_id, created_at_utc_ticks, membership_id);

            create table {schema}.organization_role_assignments (
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                assignment_id text collate "C" not null,
                user_id text collate "C" not null,
                role_id text collate "C" not null,
                organization_unit_id text collate "C" null,
                is_active boolean not null,
                created_at_utc_ticks bigint not null,
                created_at timestamptz not null,
                state_contract_version integer not null,
                state_json jsonb not null,
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, assignment_id),
                constraint ck_org_roles_tenant_scope check (
                    (tenant_scope_kind = 'global' and tenant_id = '')
                    or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                constraint ck_org_roles_contract_version check (state_contract_version = 1)
            );
            create index ix_org_roles_by_user on {schema}.organization_role_assignments (user_id, tenant_scope_kind, tenant_id, created_at_utc_ticks, assignment_id);

            create table {schema}.data_permission_scope_rules (
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                resource text collate "C" not null,
                action_match_kind integer not null,
                action_value text collate "C" not null,
                permission_match_kind integer not null,
                permission_value text collate "C" not null,
                scope_kind integer not null,
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, resource,
                    action_match_kind, action_value,
                    permission_match_kind, permission_value),
                constraint ck_data_permission_tenant_scope check (
                    (tenant_scope_kind = 'global' and tenant_id = '')
                    or (tenant_scope_kind = 'tenant' and tenant_id <> '' and tenant_id <> '*')),
                constraint ck_data_permission_action_match check (
                    (action_match_kind = 0 and action_value <> '*')
                    or (action_match_kind = 1 and action_value = '')),
                constraint ck_data_permission_permission_match check (
                    (permission_match_kind = 0 and permission_value <> '*')
                    or (permission_match_kind = 1 and permission_value = '')),
                constraint ck_data_permission_scope_kind check (scope_kind = any (array[0,1,2,3,4,5]))
            );
            """),
        new RuntimeMigration("V012", "transactional_outbox", """
            alter table {schema}.runtime_human_task_instances
                add column required_consumer_ids_json jsonb not null default '[]'::jsonb;
            update {schema}.runtime_human_task_instances
               set required_consumer_ids_json = '["crest.workflow.humantask-continuation/v1"]'::jsonb
             where workflow_instance_id is not null;
            alter table {schema}.runtime_human_task_instances
                add constraint ck_runtime_human_task_required_consumers check (jsonb_typeof(required_consumer_ids_json) = 'array');
            alter table {schema}.runtime_human_task_instances
                add constraint ck_runtime_human_task_workflow_consumer check (workflow_instance_id is null or required_consumer_ids_json @> '["crest.workflow.humantask-continuation/v1"]'::jsonb);
            alter table {schema}.runtime_human_task_instances alter column required_consumer_ids_json drop default;

            create table {schema}.runtime_outbox_messages (
                message_id text collate "C" not null,
                contract_id text collate "C" not null,
                event_name text collate "C" not null,
                event_version integer not null,
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                correlation_id text collate "C" null,
                causation_id text collate "C" null,
                occurred_at timestamptz not null,
                required_consumer_ids_json jsonb not null,
                payload_utf8 bytea not null,
                integrity_json jsonb not null,
                created_at timestamptz not null,
                available_at timestamptz not null,
                updated_at timestamptz not null,
                status integer not null default 0,
                attempt_count integer not null default 0,
                fencing_token bigint not null default 0,
                lease_owner_id text collate "C" null,
                lease_expires_at timestamptz null,
                last_failure_code text collate "C" null,
                last_failure_at timestamptz null,
                delivered_at timestamptz null,
                dead_lettered_at timestamptz null,
                terminal_lease_owner_id text collate "C" null,
                terminal_fencing_token bigint null,
                terminal_failure_code text collate "C" null,
                primary key (message_id),
                constraint ck_runtime_outbox_status check (status between 0 and 3),
                constraint ck_runtime_outbox_event_version check (event_version > 0),
                constraint ck_runtime_outbox_attempt check (attempt_count >= 0),
                constraint ck_runtime_outbox_fencing_token check (fencing_token >= 0),
                constraint ck_runtime_outbox_scope check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                constraint ck_runtime_outbox_required_consumers check (jsonb_typeof(required_consumer_ids_json) = 'array'),
                constraint ck_runtime_outbox_payload check (octet_length(payload_utf8) > 0),
                constraint ck_runtime_outbox_pending_state check (status <> 0 or (lease_owner_id is null and lease_expires_at is null and delivered_at is null and dead_lettered_at is null)),
                constraint ck_runtime_outbox_leased_state check (status <> 1 or (lease_owner_id is not null and lease_expires_at is not null and delivered_at is null and dead_lettered_at is null)),
                constraint ck_runtime_outbox_delivered_state check (status <> 2 or (delivered_at is not null and dead_lettered_at is null and lease_owner_id is null and lease_expires_at is null)),
                constraint ck_runtime_outbox_dead_letter_state check (status <> 3 or (dead_lettered_at is not null and delivered_at is null and lease_owner_id is null and lease_expires_at is null)),
                constraint ck_runtime_outbox_terminal_fence check ((status < 2 and terminal_lease_owner_id is null and terminal_fencing_token is null) or (status >= 2 and terminal_lease_owner_id is not null and terminal_fencing_token is not null))
            );
            create index ix_runtime_outbox_claim on {schema}.runtime_outbox_messages (status, available_at, lease_expires_at, occurred_at, message_id)
                where status in (0, 1);

            create table {schema}.runtime_workflow_continuation_acceptances (
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                completion_event_id text collate "C" not null,
                human_task_instance_id text collate "C" not null,
                workflow_instance_id text collate "C" not null,
                outcome text collate "C" not null,
                result_json jsonb null,
                workflow_from_revision bigint not null,
                workflow_to_revision bigint not null,
                integrity_json jsonb not null,
                receipt_json jsonb not null,
                accepted_at timestamptz not null default clock_timestamp(),
                primary key (completion_event_id),
                constraint ck_runtime_continuation_acceptance_revision check (workflow_to_revision = workflow_from_revision + 1),
                constraint ck_runtime_continuation_acceptance_tenant check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                constraint fk_continuation_acceptance_workflow foreign key (tenant_scope_kind, tenant_id, workflow_instance_id)
                    references {schema}.runtime_workflow_instances (tenant_scope_kind, tenant_id, instance_id)
                    on delete restrict deferrable initially deferred,
                constraint fk_continuation_acceptance_human_task foreign key (tenant_scope_kind, tenant_id, workflow_instance_id, human_task_instance_id)
                    references {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, instance_id)
                    on delete restrict deferrable initially deferred
            );
            create unique index uq_runtime_continuation_acceptance_task on {schema}.runtime_workflow_continuation_acceptances (tenant_scope_kind, tenant_id, human_task_instance_id);
            """),
        new RuntimeMigration("V013", "organization_scope_generation", """
            create table {schema}.organization_scope_generations (
                tenant_scope_kind text collate "C" not null,
                tenant_id text collate "C" not null,
                generation bigint not null,
                updated_at timestamptz not null,
                primary key (tenant_scope_kind, tenant_id),
                constraint ck_org_scope_generation_tenant_scope check (
                    (tenant_scope_kind = 'global' and tenant_id = '')
                    or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                constraint ck_org_scope_generation_value check (generation >= 1)
            );
            """),
    ];
}
