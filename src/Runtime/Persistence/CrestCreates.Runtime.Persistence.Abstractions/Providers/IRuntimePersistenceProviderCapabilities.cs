namespace CrestCreates.Runtime.Persistence.Abstractions.Providers;

public interface IRuntimePersistenceProviderCapabilities
{
    RuntimePersistenceProviderTier Tier { get; }

    bool SupportsAddAndCompareAndSwap { get; }

    bool SupportsAtomicMultiStoreTransactions { get; }

    bool SupportsRollback { get; }

    bool SupportsProcessDurability { get; }

    bool SupportsRestartRecovery { get; }

    bool SupportsMigrations { get; }

    bool SupportsDatabaseNativeAotEvidence { get; }
}
