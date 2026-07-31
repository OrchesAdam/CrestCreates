namespace CrestCreates.Runtime.Persistence.InMemory.Kernel;

internal sealed class InMemoryRuntimeTransactionContext
{
    public required InMemoryRuntimePersistenceState StagedState { get; init; }
}
