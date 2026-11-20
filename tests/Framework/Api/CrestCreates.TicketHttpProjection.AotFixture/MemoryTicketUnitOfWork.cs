using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// The fixture's memory unit of work over the Ticket/SLA row stores. Same shape as the proven #128
/// CRUD memory provider: a write gate serializes windows, a snapshot is restored on rollback/abandon,
/// and capabilities are declared exactly as implemented. It is bound under OrmProvider.EfCore only
/// because that enum has no memory member; no EF provider claim is made.
/// </summary>
public sealed class MemoryTicketUnitOfWork : IUnitOfWork, IUnitOfWorkAbandonable
{
    private readonly TicketMemoryStore _store;
    private readonly TicketBusinessObserver _observer;
    private readonly object _sync = new();
    private (List<TicketRow> Tickets, SlaRow? Sla)? _snapshot;
    private bool _transactionActive;
    private bool _gateHeld;

    public MemoryTicketUnitOfWork(TicketMemoryStore store, TicketBusinessObserver observer)
    {
        _store = store;
        _observer = observer;
    }

    public async Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _store.WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _gateHeld = true;
        _snapshot = _store.Snapshot();
        _transactionActive = true;
        _observer.RecordUnitOfWorkBegin();
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _snapshot = null;
            _transactionActive = false;
        }

        ReleaseGate();
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        (List<TicketRow> Tickets, SlaRow? Sla)? snapshot;
        lock (_sync)
        {
            snapshot = _transactionActive ? _snapshot : null;
            _snapshot = null;
            _transactionActive = false;
        }

        if (snapshot is { } value)
        {
            _store.Restore(value);
        }

        ReleaseGate();
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    /// <summary>
    /// 未完成退出的终结：回滚未完成窗口并丢弃其写入。快照式事务没有未 flush 的跟踪写入，
    /// 恢复快照即丢弃；无未完成工作时为 no-op，容忍重复调用。
    /// </summary>
    public void AbandonPendingWork()
    {
        (List<TicketRow> Tickets, SlaRow? Sla)? snapshot;
        lock (_sync)
        {
            snapshot = _transactionActive ? _snapshot : null;
            _snapshot = null;
            _transactionActive = false;
        }

        if (snapshot is { } value)
        {
            _store.Restore(value);
        }

        ReleaseGate();
    }

    public void Dispose() => AbandonPendingWork();

    private void ReleaseGate()
    {
        if (_gateHeld)
        {
            _gateHeld = false;
            _store.WriteGate.Release();
        }
    }
}
