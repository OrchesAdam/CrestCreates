using CrestCreates.Runtime.Persistence.Abstractions.Providers;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public sealed class PostgreSqlRuntimeProviderCapabilities : IRuntimePersistenceProviderCapabilities
{
    public RuntimePersistenceProviderTier Tier => RuntimePersistenceProviderTier.FullDurable;
    public bool SupportsAddAndCompareAndSwap => true;
    public bool SupportsAtomicMultiStoreTransactions => true;
    public bool SupportsRollback => true;
    public bool SupportsProcessDurability => true;
    public bool SupportsRestartRecovery => true;
    public bool SupportsMigrations => true;
    public bool SupportsDatabaseNativeAotEvidence => true;
}
