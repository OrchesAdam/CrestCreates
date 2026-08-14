using System.Threading;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

// Internal-only deterministic lease probe for provider contract tests.
internal static class PostgreSqlRuntimeTestHooks
{
    private static Action? _afterFirstCommandLeaseAcquired;
    private static Func<CancellationToken, ValueTask>? _beforeCommitBlock;
    private static Func<string, CancellationToken, ValueTask>? _afterWritePointBlock;

    internal static IDisposable BlockFirstCommand(Action afterLeaseAcquired)
    {
        ArgumentNullException.ThrowIfNull(afterLeaseAcquired);
        if (Interlocked.CompareExchange(ref _afterFirstCommandLeaseAcquired, afterLeaseAcquired, null) is not null)
            throw new InvalidOperationException("A PostgreSQL Runtime command lease probe is already active.");
        return new Reset();
    }

    internal static void NotifyCommandLeaseAcquired()
        => Interlocked.Exchange(ref _afterFirstCommandLeaseAcquired, null)?.Invoke();

    /// <summary>Installs a one-shot block that runs after the last durable
    /// mutation but before the provider-owned COMMIT acknowledgement.</summary>
    internal static IDisposable BlockBeforeCommit(Func<CancellationToken, ValueTask> block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (Interlocked.CompareExchange(ref _beforeCommitBlock, block, null) is not null)
            throw new InvalidOperationException("A PostgreSQL Runtime before-COMMIT probe is already active.");
        return new ResetBeforeCommit();
    }

    internal static ValueTask NotifyBeforeCommitAsync(CancellationToken cancellationToken)
    {
        var block = Interlocked.Exchange(ref _beforeCommitBlock, null);
        return block is null ? ValueTask.CompletedTask : block(cancellationToken);
    }

    /// <summary>Installs a one-shot block invoked after each named curation SQL
    /// write point, so tests can inject a failure after any individual write
    /// and prove the top-level transaction rolls the whole graph back.</summary>
    internal static IDisposable BlockAfterWritePoint(Func<string, CancellationToken, ValueTask> block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (Interlocked.CompareExchange(ref _afterWritePointBlock, block, null) is not null)
            throw new InvalidOperationException("A PostgreSQL Runtime write-point probe is already active.");
        return new ResetAfterWritePoint();
    }

    internal static bool IsAfterWritePointActive => _afterWritePointBlock is not null;

    internal static ValueTask NotifyAfterWritePointAsync(string writePoint, CancellationToken cancellationToken)
    {
        var block = Volatile.Read(ref _afterWritePointBlock);
        return block is null ? ValueTask.CompletedTask : block(writePoint, cancellationToken);
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref _afterFirstCommandLeaseAcquired, null);
    }

    private sealed class ResetBeforeCommit : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref _beforeCommitBlock, null);
    }

    private sealed class ResetAfterWritePoint : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref _afterWritePointBlock, null);
    }
}
