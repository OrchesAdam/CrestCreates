using System.Text.RegularExpressions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal static class PostgreSqlRuntimePersistenceOptionsValidator
{
    private static readonly Regex SchemaName = new(
        "^[a-z_][a-z0-9_]{0,62}$",
        RegexOptions.CultureInvariant);

    public static void Validate(PostgreSqlRuntimePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("PostgreSQL runtime connection string is required.", nameof(options));
        if (!SchemaName.IsMatch(options.Schema))
            throw new ArgumentException("PostgreSQL runtime schema must be a lower-case PostgreSQL identifier.", nameof(options));
        if (options.CommandTimeoutSeconds is <= 0 or > 600)
            throw new ArgumentOutOfRangeException(nameof(options), "PostgreSQL Runtime command timeout must be between 1 and 600 seconds.");

        var floor = options.MaximumInvocationReconciliationWindow;
        if (options.InvocationAttemptReceiptRetention < floor)
            throw new ArgumentException("InvocationAttemptReceiptRetention must be >= MaximumInvocationReconciliationWindow.", nameof(options));
        if (options.BudgetReservationRetention < floor)
            throw new ArgumentException("BudgetReservationRetention must be >= MaximumInvocationReconciliationWindow.", nameof(options));
        if (options.GovernanceCheckpointRetention < floor)
            throw new ArgumentException("GovernanceCheckpointRetention must be >= MaximumInvocationReconciliationWindow.", nameof(options));
        if (options.GovernanceFinalizationRetention < floor)
            throw new ArgumentException("GovernanceFinalizationRetention must be >= MaximumInvocationReconciliationWindow.", nameof(options));
        if (options.ReconciliationObservationRetention < floor)
            throw new ArgumentException("ReconciliationObservationRetention must be >= MaximumInvocationReconciliationWindow.", nameof(options));
        if (options.ReconciliationReceiptRetention < floor)
            throw new ArgumentException("ReconciliationReceiptRetention must be >= MaximumInvocationReconciliationWindow.", nameof(options));
        if (options.AccountabilityProjectionRetryWindow < floor)
            throw new ArgumentException("AccountabilityProjectionRetryWindow must be >= MaximumInvocationReconciliationWindow.", nameof(options));
    }
}
