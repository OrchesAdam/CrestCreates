namespace CrestCreates.Runtime.Persistence.PostgreSql;

public sealed class PostgreSqlRuntimePersistenceOptions
{
    public required string ConnectionString { get; init; }
    public string Schema { get; init; } = "crest_runtime";
    /// <summary>
    /// Explicitly enables provider-owned DDL. The default is validation only.
    /// </summary>
    public bool ApplyMigrations { get; init; }
    public int CommandTimeoutSeconds { get; init; } = 30;
}
