namespace CrestCreates.Runtime.Persistence.PostgreSql;

public sealed class PostgreSqlRuntimePersistenceOptions
{
    public required string ConnectionString { get; init; }
    public string Schema { get; init; } = "crest_runtime";
}
