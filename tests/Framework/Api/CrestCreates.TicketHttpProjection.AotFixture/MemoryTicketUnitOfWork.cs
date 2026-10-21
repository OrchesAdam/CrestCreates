using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// The fixture's memory unit of work over the Ticket/SLA row stores. Same shape as the proven #128
/// CRUD memory provider: a write gate serializes windows, a snapshot is restored on rollback/abandon,
/// and capabilities are declared exactly as implemented. It is bound under OrmProvider.EfCore only
/// because that enum has no memory member; no EF provider claim is made.
/// </summary>
public sealed class MemoryTicketUnitOfWork : IUnitOfWork
{
    private readonly TicketMemoryStore _store;
    private readonly object _sync = new();
    private (List<TicketRow> Tickets, SlaRow? Sla)? _snapshot;
    private bool _transactionActive;
    private bool _gateHeld;

    public MemoryTicketUnitOfWork(TicketMemoryStore store)
    {
        _store = store;
    }

    public async Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _store.WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _gateHeld = true;
        _snapshot = _store.Snapshot();
        _transactionActive = true;
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

    public void Dispose()
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

    private void ReleaseGate()
    {
        if (_gateHeld)
        {
            _gateHeld = false;
            _store.WriteGate.Release();
        }
    }
}
