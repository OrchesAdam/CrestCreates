using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

internal interface IOrganizationHierarchyCacheOwner
{
    ValueTask<OrganizationHierarchyAdmissionToken> AdmitScopeAsync(
        string scopeKey,
        OrganizationScopeGenerationRead generationRead,
        CancellationToken cancellationToken);

    bool TryReadSnapshot(
        OrganizationHierarchyCacheKey key,
        out OrganizationHierarchySnapshot snapshot);

    ValueTask<OrganizationHierarchySnapshot> JoinOrCreateFlightAsync(
        OrganizationHierarchyCacheKey key,
        Func<CancellationToken, ValueTask<OrganizationHierarchySnapshot>> load,
        CancellationToken cancellationToken);

    bool TryCompleteGenerationResult(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchyCacheKey key,
        OrganizationHierarchySnapshot candidate,
        bool publish,
        out OrganizationHierarchySnapshot accepted);

    bool TryCompleteUnavailableFallback(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchySnapshot requestLocalResult);
}

internal sealed class OrganizationHierarchyCacheOwner : IOrganizationHierarchyCacheOwner, IDisposable, IAsyncDisposable
{
    private readonly IOrganizationHierarchySnapshotCache _snapshotCache;
    private readonly OrganizationHierarchyCacheOptions _options;
    private readonly ConcurrentDictionary<string, OrganizationHierarchyScopeState> _safetyRegistry = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<OrganizationHierarchyCacheKey, OrganizationHierarchyFlight> _flights = new();
    private readonly CancellationTokenSource _ownerCancellation = new();
    private readonly object _safetyAdmissionGate = new();
    private int _physicalLoadCount;
    private long _revisionCounter;
    private int _disposed;
    private int _ownerCancellationDisposed;
    private int _ownerCancellationCompleted;

    public OrganizationHierarchyCacheOwner(OrganizationHierarchyCacheOptions? options = null)
        : this(options ?? new OrganizationHierarchyCacheOptions(), snapshotCache: null)
    {
    }

    internal OrganizationHierarchyCacheOwner(
        OrganizationHierarchyCacheOptions options,
        IOrganizationHierarchySnapshotCache? snapshotCache)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _snapshotCache = snapshotCache ?? new MemoryOrganizationHierarchySnapshotCache(options);
    }

    internal int ActiveLogicalFlightCount => _flights.Count;
    internal int ActivePhysicalLoadCount => Volatile.Read(ref _physicalLoadCount);
    internal int SafetyScopeCount => _safetyRegistry.Count;

    private long NextRevision() => Interlocked.Increment(ref _revisionCounter);

    public ValueTask<OrganizationHierarchyAdmissionToken> AdmitScopeAsync(
        string scopeKey,
        OrganizationScopeGenerationRead generationRead,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(scopeKey))
            throw new ArgumentException("Hierarchy cache scope key must be non-blank.", nameof(scopeKey));

        var state = GetOrAdmitSafetyState(scopeKey);
        lock (state.Gate)
            return ValueTask.FromResult(ApplyGenerationOutcome(state, generationRead));
    }

    public bool TryReadSnapshot(OrganizationHierarchyCacheKey key, out OrganizationHierarchySnapshot snapshot)
    {
        ThrowIfDisposed();
        try
        {
            return _snapshotCache.TryGet(key, out snapshot);
        }
        catch (Exception exception) when (
            Volatile.Read(ref _disposed) == 0 &&
            IsOrdinarySnapshotFailure(exception) &&
            exception is not OrganizationHierarchySnapshotCacheException)
        {
            throw new OrganizationHierarchySnapshotCacheException(
                $"Organization hierarchy snapshot lookup failed for '{key.TenantId}' generation {key.Generation}.",
                exception);
        }
    }

    public async ValueTask<OrganizationHierarchySnapshot> JoinOrCreateFlightAsync(
        OrganizationHierarchyCacheKey key,
        Func<CancellationToken, ValueTask<OrganizationHierarchySnapshot>> load,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(load);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_flights.TryGetValue(key, out var existing))
                return await existing.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (!TryReservePhysicalLoad())
            {
                throw new OrganizationException(
                    $"Organization hierarchy physical-load capacity {_options.PhysicalLoadCapacity} is exhausted.");
            }

            var created = new OrganizationHierarchyFlight(
                key,
                _options.SharedLoadTimeout,
                _ownerCancellation.Token,
                load,
                OnLogicalFlightTerminal,
                OnPhysicalLoadTerminal);

            if (_flights.TryAdd(key, created))
            {
                created.Start();
                return await created.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            created.AbandonBeforeStart();
            ReleasePhysicalLoad();
        }
    }

    public bool TryCompleteGenerationResult(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchyCacheKey key,
        OrganizationHierarchySnapshot candidate,
        bool publish,
        out OrganizationHierarchySnapshot accepted)
    {
        accepted = candidate;
        if (Volatile.Read(ref _disposed) != 0)
            return false;
        if (!_safetyRegistry.TryGetValue(token.ScopeKey, out var state))
            return false;

        if (!publish)
        {
            lock (state.Gate)
            {
                return Volatile.Read(ref _disposed) == 0 &&
                       IsCandidateAdmissible(state, candidate) &&
                       state.Mode == OrganizationHierarchySafetyMode.Normal;
            }
        }

        // Snapshot infrastructure is deliberately outside the safety-state
        // lock. Publication can block or fail, and observations must remain
        // able to advance/regress/quarantine the scope while it is in flight.
        // The second lock below is the mandatory final caller-completion gate.
        var published = false;
        try
        {
            _snapshotCache.Set(key, candidate);
            published = true;
        }
        catch (Exception exception) when (
            Volatile.Read(ref _disposed) == 0 &&
            IsOrdinarySnapshotFailure(exception))
        {
            // A normal-scope candidate may still be returned request-locally,
            // but only if the final gate remains admissible. Quarantine cannot
            // be released without a successful publication.
        }

        lock (state.Gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;
            if (!IsCandidateAdmissible(state, candidate))
                return false;

            if (!published)
                return state.Mode == OrganizationHierarchySafetyMode.Normal;

            if (state.Mode == OrganizationHierarchySafetyMode.Quarantined)
            {
                state.Update(
                    OrganizationHierarchySafetyMode.Normal,
                    state.ObservedHighWater,
                    quarantineFloor: null,
                    NextRevision());
            }

            return Volatile.Read(ref _disposed) == 0;
        }
    }

    private static bool IsCandidateAdmissible(
        OrganizationHierarchyScopeState state,
        OrganizationHierarchySnapshot candidate)
        => state.ObservedHighWater.HasValue &&
           candidate.Generation == state.ObservedHighWater.Value &&
           (state.Mode != OrganizationHierarchySafetyMode.Quarantined ||
            (state.QuarantineFloor.HasValue && candidate.Generation > state.QuarantineFloor.Value));

    public bool TryCompleteUnavailableFallback(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchySnapshot requestLocalResult)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;
        if (!_safetyRegistry.TryGetValue(token.ScopeKey, out var state))
            return false;

        lock (state.Gate)
        {
            return Volatile.Read(ref _disposed) == 0 &&
                   state.Mode == OrganizationHierarchySafetyMode.Normal &&
                   token.Mode == OrganizationHierarchySafetyMode.Normal &&
                   state.ObservedHighWater == token.ObservedHighWater;
        }
    }

    private OrganizationHierarchyScopeState GetOrAdmitSafetyState(string scopeKey)
    {
        if (_safetyRegistry.TryGetValue(scopeKey, out var existing))
            return existing;

        lock (_safetyAdmissionGate)
        {
            if (_safetyRegistry.TryGetValue(scopeKey, out existing))
                return existing;
            if (_safetyRegistry.Count >= _options.SafetyScopeCapacity)
            {
                throw new OrganizationException(
                    $"Organization hierarchy safety-scope capacity {_options.SafetyScopeCapacity} is exhausted.");
            }

            var created = new OrganizationHierarchyScopeState(scopeKey, NextRevision());
            if (!_safetyRegistry.TryAdd(scopeKey, created))
                return _safetyRegistry[scopeKey];
            return created;
        }
    }

    private OrganizationHierarchyAdmissionToken ApplyGenerationOutcome(
        OrganizationHierarchyScopeState state,
        OrganizationScopeGenerationRead generationRead)
    {
        if (generationRead.Status == OrganizationScopeGenerationStatus.Unavailable)
        {
            if (generationRead.Generation != 0)
                throw InvalidGenerationOutcome(state, "Unavailable generation outcome must use canonical generation 0.");
            if (state.Mode == OrganizationHierarchySafetyMode.Quarantined)
            {
                throw new OrganizationHierarchyFreshnessException(
                    OrganizationHierarchyFreshnessFailureKind.QuarantinedGenerationUnavailable,
                    observedHighWaterGeneration: state.ObservedHighWater,
                    quarantineFloorGeneration: state.QuarantineFloor,
                    message: "Quarantined hierarchy scope cannot use direct availability fallback.");
            }

            return Token(state, generation: null);
        }

        if (generationRead.Status != OrganizationScopeGenerationStatus.Available || generationRead.Generation < 0)
            throw InvalidGenerationOutcome(state, "Unknown, undefined, or malformed generation outcome cannot authorize a hierarchy read.");

        var generation = generationRead.Generation;
        var revision = NextRevision();

        if (state.Mode == OrganizationHierarchySafetyMode.Normal)
        {
            if (state.ObservedHighWater.HasValue && generation < state.ObservedHighWater.Value)
            {
                var regressionFloor = state.ObservedHighWater.Value;
                state.Update(OrganizationHierarchySafetyMode.Quarantined, regressionFloor, regressionFloor, revision);
                throw Regression(state, generation, "Observed generation regressed below the process high-water mark.");
            }

            var highWater = !state.ObservedHighWater.HasValue || generation > state.ObservedHighWater.Value
                ? generation
                : state.ObservedHighWater.Value;
            state.Update(OrganizationHierarchySafetyMode.Normal, highWater, null, revision);
            return Token(state, generation);
        }

        var observedHighWater = state.ObservedHighWater
            ?? throw InvalidGenerationOutcome(state, "Quarantined scope has no observed high-water mark.");
        var floor = state.QuarantineFloor
            ?? throw InvalidGenerationOutcome(state, "Quarantined scope has no quarantine floor.");

        if (generation < observedHighWater || generation <= floor)
            throw Regression(state, generation, "Generation is not eligible to recover the quarantined hierarchy scope.");

        if (generation > observedHighWater)
            state.Update(OrganizationHierarchySafetyMode.Quarantined, generation, floor, revision);

        return Token(state, generation);
    }

    private static OrganizationHierarchyAdmissionToken Token(
        OrganizationHierarchyScopeState state,
        long? generation)
        => new(
            state.ScopeKey,
            state.Revision,
            state.Mode,
            state.ObservedHighWater,
            state.QuarantineFloor,
            generation);

    private static OrganizationHierarchyFreshnessException Regression(
        OrganizationHierarchyScopeState state,
        long generation,
        string message)
        => new(
            OrganizationHierarchyFreshnessFailureKind.GenerationRegression,
            observedGeneration: generation,
            observedHighWaterGeneration: state.ObservedHighWater,
            quarantineFloorGeneration: state.QuarantineFloor,
            message: message);

    private static OrganizationHierarchyFreshnessException InvalidGenerationOutcome(
        OrganizationHierarchyScopeState state,
        string message)
        => new(
            OrganizationHierarchyFreshnessFailureKind.InvalidGenerationOutcome,
            observedHighWaterGeneration: state.ObservedHighWater,
            quarantineFloorGeneration: state.QuarantineFloor,
            message: message);

    private bool TryReservePhysicalLoad()
    {
        while (true)
        {
            var current = Volatile.Read(ref _physicalLoadCount);
            if (current >= _options.PhysicalLoadCapacity)
                return false;
            if (Interlocked.CompareExchange(ref _physicalLoadCount, current + 1, current) == current)
                return true;
        }
    }

    private void ReleasePhysicalLoad() => Interlocked.Decrement(ref _physicalLoadCount);

    private void OnLogicalFlightTerminal(OrganizationHierarchyFlight flight)
    {
        if (_flights.TryGetValue(flight.Key, out var current) && ReferenceEquals(current, flight))
            _flights.TryRemove(flight.Key, out _);
    }

    private void OnPhysicalLoadTerminal(OrganizationHierarchyFlight _)
    {
        ReleasePhysicalLoad();
        TryDisposeOwnerCancellation();
    }

    private void TryDisposeOwnerCancellation()
    {
        if (Volatile.Read(ref _disposed) == 0 ||
            Volatile.Read(ref _ownerCancellationCompleted) == 0 ||
            Volatile.Read(ref _physicalLoadCount) != 0)
            return;
        if (Interlocked.Exchange(ref _ownerCancellationDisposed, 1) == 0)
            _ownerCancellation.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(OrganizationHierarchyCacheOwner));
    }

    private static bool IsOrdinarySnapshotFailure(Exception exception)
        => exception is not (OutOfMemoryException or AccessViolationException);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _ownerCancellation.Cancel();
        }
        finally
        {
            Volatile.Write(ref _ownerCancellationCompleted, 1);
        }
        foreach (var flight in _flights.Values)
            flight.InvalidateForOwnerDisposal();
        _snapshotCache.Dispose();
        // Keep the CTS alive while non-cooperative physical loads still hold
        // linked tokens; the last physical terminal disposes it.
        TryDisposeOwnerCancellation();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
