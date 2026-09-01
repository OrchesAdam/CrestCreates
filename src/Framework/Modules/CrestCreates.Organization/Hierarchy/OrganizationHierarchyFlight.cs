namespace CrestCreates.Organization;

/// <summary>
/// Separates the joinable logical attempt from the underlying physical Store
/// operation. Logical timeout invalidates the result immediately, while the
/// physical-capacity lease is retained until the Store task is actually terminal.
/// </summary>
internal sealed class OrganizationHierarchyFlight
{
    private readonly Func<CancellationToken, ValueTask<OrganizationHierarchySnapshot>> _load;
    private readonly CancellationTokenSource _loadCancellation;
    private readonly CancellationTokenSource _timeoutCancellation = new();
    private readonly TimeSpan _timeout;
    private readonly Action<OrganizationHierarchyFlight> _logicalTerminal;
    private readonly Action<OrganizationHierarchyFlight> _physicalTerminal;
    private readonly TaskCompletionSource<OrganizationHierarchySnapshot> _logicalCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _logicalState;
    private int _started;
    private int _physicalCleanup;

    public OrganizationHierarchyFlight(
        OrganizationHierarchyCacheKey key,
        TimeSpan timeout,
        CancellationToken ownerCancellation,
        Func<CancellationToken, ValueTask<OrganizationHierarchySnapshot>> load,
        Action<OrganizationHierarchyFlight> logicalTerminal,
        Action<OrganizationHierarchyFlight> physicalTerminal)
    {
        Key = key;
        _timeout = timeout;
        _load = load;
        _logicalTerminal = logicalTerminal;
        _physicalTerminal = physicalTerminal;
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(ownerCancellation);
    }

    public OrganizationHierarchyCacheKey Key { get; }

    public Task<OrganizationHierarchySnapshot> Completion => _logicalCompletion.Task;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("Organization hierarchy flight was already started.");

        _ = RunPhysicalLoadAsync();
        _ = WatchTimeoutAsync();
    }

    public async ValueTask<OrganizationHierarchySnapshot> WaitAsync(CancellationToken cancellationToken)
        => await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);

    public void InvalidateForOwnerDisposal()
    {
        if (TryEndLogical())
            _logicalCompletion.TrySetException(new ObjectDisposedException(nameof(OrganizationHierarchyCacheOwner)));
        TryCancelLoad();
    }

    public void AbandonBeforeStart()
    {
        if (Interlocked.Exchange(ref _physicalCleanup, 1) != 0)
            return;
        _timeoutCancellation.Cancel();
        _timeoutCancellation.Dispose();
        _loadCancellation.Dispose();
    }

    private async Task WatchTimeoutAsync()
    {
        try
        {
            await Task.Delay(_timeout, _timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!TryEndLogical())
            return;

        _logicalCompletion.TrySetException(
            new TimeoutException($"Organization hierarchy authority load timed out for '{Key.TenantId}' generation {Key.Generation}."));
        TryCancelLoad();
    }

    private async Task RunPhysicalLoadAsync()
    {
        try
        {
            var snapshot = await _load(_loadCancellation.Token).ConfigureAwait(false);
            if (TryEndLogical())
                _logicalCompletion.TrySetResult(snapshot);
        }
        catch (OperationCanceledException exception)
        {
            if (TryEndLogical())
                _logicalCompletion.TrySetException(exception);
        }
        catch (Exception exception)
        {
            if (TryEndLogical())
                _logicalCompletion.TrySetException(exception);
        }
        finally
        {
            _timeoutCancellation.Cancel();
            CompletePhysicalCleanup();
        }
    }

    private bool TryEndLogical()
    {
        if (Interlocked.CompareExchange(ref _logicalState, 1, 0) != 0)
            return false;
        _logicalTerminal(this);
        return true;
    }

    private void CompletePhysicalCleanup()
    {
        if (Interlocked.Exchange(ref _physicalCleanup, 1) != 0)
            return;
        _timeoutCancellation.Dispose();
        _loadCancellation.Dispose();
        _physicalTerminal(this);
    }

    private void TryCancelLoad()
    {
        try
        {
            _loadCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Physical completion won the race and already released resources.
        }
    }
}
