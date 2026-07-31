namespace CrestCreates.Runtime.Persistence.InMemory.Kernel;

internal sealed class InMemoryRuntimeTransactionAccessor
{
    private readonly AsyncLocal<InMemoryRuntimeTransactionContext?> _current = new();
    public InMemoryRuntimeTransactionContext? Current => _current.Value;
    public void Set(InMemoryRuntimeTransactionContext? context) => _current.Value = context;
}
