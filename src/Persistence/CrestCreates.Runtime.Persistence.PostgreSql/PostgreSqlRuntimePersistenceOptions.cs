using System.ComponentModel.DataAnnotations;

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

    /// <summary>
    /// Maximum window during which an invocation attempt may be reconciled.
    /// Every dependent retention must be >= this value.
    /// Default: 7 days.
    /// </summary>
    public TimeSpan MaximumInvocationReconciliationWindow { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan InvocationAttemptReceiptRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan BudgetReservationRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan GovernanceCheckpointRetention { get; init; } = TimeSpan.FromDays(90);
    public TimeSpan GovernanceFinalizationRetention { get; init; } = TimeSpan.FromDays(90);
    public TimeSpan ReconciliationObservationRetention { get; init; } = TimeSpan.FromDays(14);
    public TimeSpan ReconciliationReceiptRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan AccountabilityProjectionRetryWindow { get; init; } = TimeSpan.FromDays(7);
}
