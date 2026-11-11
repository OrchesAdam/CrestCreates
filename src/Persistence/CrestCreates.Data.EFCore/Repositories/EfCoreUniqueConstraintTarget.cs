using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.Repositories;

/// <summary>
/// Physical identity of an entity's unique indexes as reached through the live EF model, so a
/// provider-reported violation can be attributed to the actual mapped relation instead of a
/// hard-coded table name. Business DbContexts may remap tables with <c>ToTable</c>.
/// </summary>
internal sealed class EfCoreUniqueConstraintTarget
{
    private readonly Func<string?> _defaultSchemaResolver;

    private EfCoreUniqueConstraintTarget(
        string? schema,
        string table,
        IReadOnlyList<IReadOnlyList<string>> keyColumnSets,
        IReadOnlyList<string> indexNames,
        Func<string?> defaultSchemaResolver)
    {
        Schema = schema;
        Table = table;
        KeyColumnSets = keyColumnSets;
        IndexNames = indexNames;
        _defaultSchemaResolver = defaultSchemaResolver;
    }

    public string? Schema { get; }

    public string Table { get; }

    /// <summary>Mapped column names per unique index, used by providers that only expose columns.</summary>
    public IReadOnlyList<IReadOnlyList<string>> KeyColumnSets { get; }

    /// <summary>Database names of the unique indexes, used by providers that expose the constraint.</summary>
    public IReadOnlyList<string> IndexNames { get; }

    public static EfCoreUniqueConstraintTarget? TryCreate<TEntity>(DbContext dbContext)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        var table = entityType?.GetTableName();
        if (entityType is null || table is null)
        {
            return null;
        }

        var uniqueIndexes = entityType.GetIndexes().Where(index => index.IsUnique).ToList();
        var keyColumnSets = uniqueIndexes
            .Select(index => index.Properties.Select(property => property.GetColumnName()).ToArray())
            .Where(columns => columns.Length > 0 && columns.All(column => column is not null))
            .Select(columns => (IReadOnlyList<string>)columns!)
            .ToList();
        var indexNames = uniqueIndexes
            .Select(index => index.GetDatabaseName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();

        return new EfCoreUniqueConstraintTarget(
            entityType.GetSchema(),
            table,
            keyColumnSets,
            indexNames,
            CreateDefaultSchemaResolver(dbContext, table));
    }

    /// <summary>
    /// Effective schema of an unqualified model mapping. PostgreSQL resolves an unqualified table
    /// to the first relation found along the connection's search_path, which is not necessarily
    /// the search_path's first schema (<c>current_schema()</c>); an unresolved relation stays null
    /// and the caller fails closed.
    /// </summary>
    public string? ResolveSchema()
    {
        return Schema ?? _defaultSchemaResolver();
    }

    private static Func<string?> CreateDefaultSchemaResolver(DbContext dbContext, string table)
    {
        if (!string.Equals(
                dbContext.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
        {
            return static () => null;
        }

        return () => ReadNpgsqlResolvedSchema(dbContext, table);
    }

    private static string? ReadNpgsqlResolvedSchema(DbContext dbContext, string table)
    {
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            var wasClosed = connection.State != ConnectionState.Open;
            if (wasClosed)
            {
                connection.Open();
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT namespace.nspname
                    FROM pg_catalog.pg_class AS relation
                    JOIN pg_catalog.pg_namespace AS namespace
                      ON namespace.oid = relation.relnamespace
                    WHERE relation.oid = pg_catalog.to_regclass(@relation);
                    """;
                var relationName = command.CreateParameter();
                relationName.ParameterName = "relation";
                relationName.Value = "\"" + table.Replace("\"", "\"\"") + "\"";
                command.Parameters.Add(relationName);
                return command.ExecuteScalar() as string;
            }
            finally
            {
                if (wasClosed)
                {
                    connection.Close();
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
