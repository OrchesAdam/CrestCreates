using CrestCreates.Runtime.Persistence.Abstractions.Providers;

namespace CrestCreates.Runtime.Persistence.InMemory;

public sealed class InMemoryRuntimeProviderCapabilities : IRuntimePersistenceProviderCapabilities
{
    public RuntimePersistenceProviderTier Tier => RuntimePersistenceProviderTier.FullSemantic;
    public bool SupportsAddAndCompareAndSwap => true;
    public bool SupportsAtomicMultiStoreTransactions => true;
    public bool SupportsRollback => true;
    public bool SupportsProcessDurability => false;
    public bool SupportsRestartRecovery => false;
    public bool SupportsMigrations => false;
    public bool SupportsDatabaseNativeAotEvidence => false;
}
