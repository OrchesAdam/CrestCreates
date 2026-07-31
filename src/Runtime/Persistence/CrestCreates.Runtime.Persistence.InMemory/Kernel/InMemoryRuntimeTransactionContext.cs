using System.Threading;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;

namespace CrestCreates.Runtime.Persistence.InMemory.Kernel;

internal sealed class InMemoryRuntimeTransactionContext
{
    private int _inUse;

    public required InMemoryRuntimePersistenceState StagedState { get; init; }

    public IDisposable EnterOperation()
    {
        if (Interlocked.CompareExchange(ref _inUse, 1, 0) != 0)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.ConcurrentAmbientUse,
                "Concurrent use of one ambient Runtime transaction is not supported.");
        }
        return new Releaser(this);
    }

    private sealed class Releaser(InMemoryRuntimeTransactionContext owner) : IDisposable
    {
        private InMemoryRuntimeTransactionContext? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
                Volatile.Write(ref owner._inUse, 0);
        }
    }
}
