using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.GeneratedCrud.AotFixture.Domain;

namespace CrestCreates.GeneratedCrud.AotFixture;

/// <summary>
/// #128 S4 Step B memory unit of work: the provider the generated [UnitOfWorkMo] write methods
/// actually run under once the Feature assembly is woven. It gives the memory store a real — if
/// coarse — transaction: a write window serialized by the store's gate plus a pre-transaction row
/// snapshot. Commit drops the snapshot; rollback (and an unfinished dispose, i.e. abandon) restores
/// it, so everything a rolled-back request wrote is discarded. Declared capabilities match exactly
/// what this implementation does: transactions yes, no explicit isolation levels (undeclared means
/// unsupported — the kernel fail-closes before business code), prompt termination and discard on
/// abandon. It is bound under <c>OrmProvider.EfCore</c> only because that enum is a closed list of
/// ORM families and this fixture store belongs to none of them — the same fixture-binding shape the
/// #137 Data.Abstractions native fixture uses for its static unit of work; no claim is made about a
/// real EF provider.
/// </summary>
public sealed class MemoryWidgetUnitOfWork : IUnitOfWork
{
    private readonly MemoryWidgetRepository _repository;
    private readonly MemoryUnitOfWorkObserver _observer;
    private readonly object _sync = new();
    private List<Widget>? _snapshot;
    private bool _transactionActive;
    private bool _gateHeld;

    public MemoryWidgetUnitOfWork(MemoryWidgetRepository repository, MemoryUnitOfWorkObserver observer)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
    }

    public async Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The window owns the store gate until commit/rollback/dispose, so a snapshot restore can
        // never clobber a concurrent window's writes (windows serialize; reads stay lock-scoped).
        await _repository.WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _gateHeld = true;
        _snapshot = _repository.SnapshotRows();
        _transactionActive = true;
        _observer.RecordBegin();
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _snapshot = null;
            _transactionActive = false;
        }

        ReleaseGate();
        _observer.RecordCommit();
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        List<Widget>? snapshot;
        lock (_sync)
        {
            snapshot = _transactionActive ? _snapshot : null;
            _snapshot = null;
            _transactionActive = false;
        }

        if (snapshot is not null)
        {
            _repository.RestoreRows(snapshot);
        }

        ReleaseGate();
        _observer.RecordRollback();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes apply to the store inside the transaction window; the commit/rollback boundary is the
    /// snapshot, not a deferred flush — there are never pending tracked changes to write here.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public void Dispose()
    {
        // Unfinished exit (abandon): discard whatever this window wrote. The kernel's own abandon
        // protocol rolls back first, so this restore usually finds nothing left to do — it exists so
        // an interrupted frame can never leak a half-written window into the store.
        List<Widget>? snapshot;
        lock (_sync)
        {
            snapshot = _transactionActive ? _snapshot : null;
            _snapshot = null;
            _transactionActive = false;
        }

        if (snapshot is not null)
        {
            _repository.RestoreRows(snapshot);
            _observer.RecordRollback();
        }

        ReleaseGate();
    }

    private void ReleaseGate()
    {
        if (_gateHeld)
        {
            _gateHeld = false;
            _repository.WriteGate.Release();
        }
    }
}

/// <summary>
/// #128 S4 Step B unit-of-work facts: one process-wide observation point for the fixture host so the
/// HTTP tests can assert what the woven frames did (begin/commit/rollback) per request.
/// </summary>
public sealed class MemoryUnitOfWorkObserver
{
    private int _beginCount;
    private int _commitCount;
    private int _rollbackCount;

    public void RecordBegin() => Interlocked.Increment(ref _beginCount);

    public void RecordCommit() => Interlocked.Increment(ref _commitCount);

    public void RecordRollback() => Interlocked.Increment(ref _rollbackCount);

    public UnitOfWorkFacts Capture() => new(
        Volatile.Read(ref _beginCount),
        Volatile.Read(ref _commitCount),
        Volatile.Read(ref _rollbackCount));
}

public readonly record struct UnitOfWorkFacts(int BeginCount, int CommitCount, int RollbackCount);
