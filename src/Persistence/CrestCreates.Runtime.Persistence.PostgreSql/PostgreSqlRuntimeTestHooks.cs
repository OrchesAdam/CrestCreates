using System.Threading;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

// Internal-only deterministic lease probe for provider contract tests.
internal static class PostgreSqlRuntimeTestHooks
{
    private static Action? _afterFirstCommandLeaseAcquired;

    internal static IDisposable BlockFirstCommand(Action afterLeaseAcquired)
    {
        ArgumentNullException.ThrowIfNull(afterLeaseAcquired);
        if (Interlocked.CompareExchange(ref _afterFirstCommandLeaseAcquired, afterLeaseAcquired, null) is not null)
            throw new InvalidOperationException("A PostgreSQL Runtime command lease probe is already active.");
        return new Reset();
    }

    internal static void NotifyCommandLeaseAcquired()
        => Interlocked.Exchange(ref _afterFirstCommandLeaseAcquired, null)?.Invoke();

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref _afterFirstCommandLeaseAcquired, null);
    }
}
