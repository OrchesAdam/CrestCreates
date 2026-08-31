using System.Collections.Concurrent;

namespace CrestCreates.Organization;

internal sealed class OrganizationHierarchyFlight
{
    private readonly OrganizationHierarchyCacheKey _key;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private readonly ConcurrentBag<OrganizationHierarchyWaiter> _waiters = new();

    private bool _ownerSet;
    private bool _isOwner;
    private OrganizationHierarchySnapshot? _result;
    private Exception? _exception;
    private bool _completed;
    private bool _timedOut;

    public OrganizationHierarchyFlight(OrganizationHierarchyCacheKey key, TimeSpan timeout)
    {
        _key = key;
        _timeout = timeout;
    }

    public bool TryJoin(out OrganizationHierarchyWaiter waiter)
    {
        lock (_gate)
        {
            if (_completed || _timedOut)
            {
                waiter = new OrganizationHierarchyWaiter();
                return false;
            }

            if (!_ownerSet)
            {
                _ownerSet = true;
                _isOwner = true;
                waiter = new OrganizationHierarchyWaiter();
                return true;
            }

            waiter = new OrganizationHierarchyWaiter();
            _waiters.Add(waiter);
            return false;
        }
    }

    public void Complete(OrganizationHierarchySnapshot snapshot)
    {
        List<OrganizationHierarchyWaiter> waiters;
        lock (_gate)
        {
            if (_completed || _timedOut) return;
            _completed = true;
            _result = snapshot;
            _exception = null;
            waiters = _waiters.ToList();
            _waiters.Clear();
        }

        foreach (var w in waiters)
            w.TrySetResult(new OrganizationHierarchyFlightResult(snapshot, false, false));
    }

    public void Fail(Exception exception)
    {
        List<OrganizationHierarchyWaiter> waiters;
        lock (_gate)
        {
            if (_completed || _timedOut) return;
            _completed = true;
            _result = null;
            _exception = exception;
            waiters = _waiters.ToList();
            _waiters.Clear();
        }

        foreach (var w in waiters)
            w.TrySetResult(new OrganizationHierarchyFlightResult(null, false, true));
    }

    public void TimeOut()
    {
        List<OrganizationHierarchyWaiter> waiters;
        lock (_gate)
        {
            if (_completed) return;
            _timedOut = true;
            waiters = _waiters.ToList();
            _waiters.Clear();
        }

        foreach (var w in waiters)
            w.TrySetResult(new OrganizationHierarchyFlightResult(null, true, false));
    }

    public ValueTask<OrganizationHierarchyFlightResult> WaitAsync(CancellationToken cancellationToken)
    {
        OrganizationHierarchyWaiter? waiter = null;
        lock (_gate)
        {
            if (_completed)
            {
                return new ValueTask<OrganizationHierarchyFlightResult>(
                    new OrganizationHierarchyFlightResult(_result, false, _exception != null));
            }

            if (_timedOut)
            {
                return new ValueTask<OrganizationHierarchyFlightResult>(
                    new OrganizationHierarchyFlightResult(null, true, false));
            }

            waiter = new OrganizationHierarchyWaiter();
            _waiters.Add(waiter);
        }

        return waiter.WaitAsync(_timeout, cancellationToken);
    }
}

internal sealed class OrganizationHierarchyWaiter
{
    private readonly TaskCompletionSource<OrganizationHierarchyFlightResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _ctr;

    public void TrySetResult(OrganizationHierarchyFlightResult result)
    {
        _tcs.TrySetResult(result);
    }

    public ValueTask<OrganizationHierarchyFlightResult> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            _ctr = cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
        }

        return new ValueTask<OrganizationHierarchyFlightResult>(
            _tcs.Task.WaitAsync(timeout, cancellationToken));
    }
}

internal readonly record struct OrganizationHierarchyFlightResult(
    OrganizationHierarchySnapshot? Snapshot,
    bool TimedOut,
    bool Failed);
