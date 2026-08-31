using System.Collections.Concurrent;
using System.Collections.Immutable;
using CrestCreates.Organization.Abstractions;
using Microsoft.Extensions.Caching.Memory;

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

    ValueTask<OrganizationHierarchyLoadResult> JoinOrCreateFlightAsync(
        OrganizationHierarchyCacheKey key,
        Func<CancellationToken, ValueTask<OrganizationHierarchySnapshot>> load,
        CancellationToken cancellationToken);

    bool TryCompleteGenerationResult(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchyCacheKey key,
        OrganizationHierarchySnapshot candidate,
        out OrganizationHierarchySnapshot accepted);

    bool TryCompleteUnavailableFallback(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchySnapshot requestLocalResult);

    bool TryCompleteCacheFailureFallback(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchyCacheKey key,
        OrganizationHierarchySnapshot requestLocalResult);

    long GetPublicationGeneration(string tenantId);
}

internal readonly record struct OrganizationHierarchyLoadResult(
    bool IsOwner,
    OrganizationHierarchySnapshot? Snapshot,
    bool TimedOut,
    bool Failed);

internal sealed class OrganizationHierarchyCacheOwner : IOrganizationHierarchyCacheOwner, IDisposable, IAsyncDisposable
{
    private readonly IMemoryCache _snapshotCache;
    private readonly OrganizationHierarchyCacheOptions _options;
    private readonly ConcurrentDictionary<string, OrganizationHierarchyScopeState> _safetyRegistry = new();
    private readonly ConcurrentDictionary<OrganizationHierarchyCacheKey, OrganizationHierarchyFlight> _flights = new();
    private int _activeLoadCount;
    private int _revisionCounter;
    private bool _disposed;

    public OrganizationHierarchyCacheOwner(OrganizationHierarchyCacheOptions? options = null)
    {
        _options = options ?? new OrganizationHierarchyCacheOptions();
        _snapshotCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.SnapshotCapacity,
            CompactionPercentage = 0.25
        });
    }

    private long NextRevision() => Interlocked.Increment(ref _revisionCounter);

    public ValueTask<OrganizationHierarchyAdmissionToken> AdmitScopeAsync(
        string scopeKey,
        OrganizationScopeGenerationRead generationRead,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = _safetyRegistry.GetOrAdd(scopeKey, key => new OrganizationHierarchyScopeState(key, NextRevision()));
        lock (state.Gate)
        {
            var outcome = ApplyGenerationOutcome(state, generationRead);
            return new ValueTask<OrganizationHierarchyAdmissionToken>(outcome);
        }
    }

    private OrganizationHierarchyAdmissionToken ApplyGenerationOutcome(
        OrganizationHierarchyScopeState state,
        OrganizationScopeGenerationRead generationRead)
    {
        var revision = NextRevision();

        if (generationRead.Status == OrganizationScopeGenerationStatus.Unknown)
        {
            throw new OrganizationHierarchyFreshnessException(
                OrganizationHierarchyFreshnessFailureKind.InvalidGenerationOutcome,
                message: "default/Unknown generation outcome cannot authorize hierarchy read.");
        }

        if (generationRead.Status == OrganizationScopeGenerationStatus.Unavailable)
        {
            if (state.Mode == OrganizationHierarchySafetyMode.Quarantined)
            {
                throw new OrganizationHierarchyFreshnessException(
                    OrganizationHierarchyFreshnessFailureKind.QuarantinedGenerationUnavailable,
                    observedHighWaterGeneration: state.ObservedHighWater,
                    quarantineFloorGeneration: state.QuarantineFloor,
                    message: "quarantined scope cannot fall back to direct authority.");
            }

            return new OrganizationHierarchyAdmissionToken(
                state.ScopeKey,
                revision,
                state.Mode,
                state.ObservedHighWater,
                state.QuarantineFloor,
                Generation: null);
        }

        // Available(G)
        var g = generationRead.Generation;

        if (state.Mode == OrganizationHierarchySafetyMode.Normal)
        {
            if (state.ObservedHighWater.HasValue && g < state.ObservedHighWater.Value)
            {
                // Regression: capture floor, quarantine
                var floor = state.ObservedHighWater.Value;
                state.Update(OrganizationHierarchySafetyMode.Quarantined, state.ObservedHighWater, floor, revision);
                throw new OrganizationHierarchyFreshnessException(
                    OrganizationHierarchyFreshnessFailureKind.GenerationRegression,
                    observedGeneration: g,
                    observedHighWaterGeneration: state.ObservedHighWater,
                    quarantineFloorGeneration: floor,
                    message: "observed generation regression below ObservedHighWater.");
            }

            // Advance high-water
            var newHighWater = !state.ObservedHighWater.HasValue || g > state.ObservedHighWater.Value
                ? g
                : state.ObservedHighWater.Value;
            state.Update(OrganizationHierarchySafetyMode.Normal, newHighWater, null, revision);

            return new OrganizationHierarchyAdmissionToken(
                state.ScopeKey,
                revision,
                OrganizationHierarchySafetyMode.Normal,
                newHighWater,
                null,
                Generation: g);
        }

        // QUARANTINED
        if (g < state.ObservedHighWater)
        {
            state.Update(OrganizationHierarchySafetyMode.Quarantined, state.ObservedHighWater, state.QuarantineFloor, revision);
            throw new OrganizationHierarchyFreshnessException(
                OrganizationHierarchyFreshnessFailureKind.GenerationRegression,
                observedGeneration: g,
                observedHighWaterGeneration: state.ObservedHighWater,
                quarantineFloorGeneration: state.QuarantineFloor,
                message: "quarantined scope: generation below ObservedHighWater.");
        }

        if (g <= state.QuarantineFloor)
        {
            state.Update(OrganizationHierarchySafetyMode.Quarantined, state.ObservedHighWater, state.QuarantineFloor, revision);
            throw new OrganizationHierarchyFreshnessException(
                OrganizationHierarchyFreshnessFailureKind.GenerationRegression,
                observedGeneration: g,
                observedHighWaterGeneration: state.ObservedHighWater,
                quarantineFloorGeneration: state.QuarantineFloor,
                message: "quarantined scope: generation at/below QuarantineFloor.");
        }

        if (g > state.ObservedHighWater)
        {
            // Advance high-water, remain quarantined (recovery not yet published)
            state.Update(OrganizationHierarchySafetyMode.Quarantined, g, state.QuarantineFloor, revision);
            return new OrganizationHierarchyAdmissionToken(
                state.ScopeKey,
                revision,
                OrganizationHierarchySafetyMode.Quarantined,
                g,
                state.QuarantineFloor,
                Generation: g);
        }

        // g == ObservedHighWater && g > QuarantineFloor → eligible to retry
        return new OrganizationHierarchyAdmissionToken(
            state.ScopeKey,
            revision,
            OrganizationHierarchySafetyMode.Quarantined,
            state.ObservedHighWater,
            state.QuarantineFloor,
            Generation: g);
    }

    public bool TryReadSnapshot(OrganizationHierarchyCacheKey key, out OrganizationHierarchySnapshot snapshot)
    {
        return _snapshotCache.TryGetValue(key, out snapshot!);
    }

    public async ValueTask<OrganizationHierarchyLoadResult> JoinOrCreateFlightAsync(
        OrganizationHierarchyCacheKey key,
        Func<CancellationToken, ValueTask<OrganizationHierarchySnapshot>> load,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var flight = _flights.GetOrAdd(key, _ => new OrganizationHierarchyFlight(key, _options.SharedLoadTimeout));

            if (flight.TryJoin(out var waiter))
            {
                try
                {
                    var snapshot = await load(cancellationToken).ConfigureAwait(false);
                    flight.Complete(snapshot);
                    return new OrganizationHierarchyLoadResult(true, snapshot, false, false);
                }
                catch (Exception ex)
                {
                    flight.Fail(ex);
                    return new OrganizationHierarchyLoadResult(true, null, false, true);
                }
            }
            else
            {
                // Joined as waiter
                var result = await flight.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (result.TimedOut)
                {
                    return new OrganizationHierarchyLoadResult(false, null, true, false);
                }

                if (result.Failed)
                {
                    // Retry: create a new flight
                    _flights.TryRemove(key, out _);
                    continue;
                }

                return new OrganizationHierarchyLoadResult(false, result.Snapshot, false, false);
            }
        }
    }

    public bool TryCompleteGenerationResult(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchyCacheKey key,
        OrganizationHierarchySnapshot candidate,
        out OrganizationHierarchySnapshot accepted)
    {
        if (!_safetyRegistry.TryGetValue(token.ScopeKey, out var state))
        {
            accepted = candidate;
            return false;
        }

        lock (state.Gate)
        {
            // Revalidate: revision must still match and state must admit this generation
            if (state.Revision != token.Revision && token.Mode != OrganizationHierarchySafetyMode.Quarantined)
            {
                // State changed since admission
                if (state.Mode == OrganizationHierarchySafetyMode.Quarantined &&
                    candidate.Generation <= state.QuarantineFloor)
                {
                    accepted = candidate;
                    return false;
                }
                if (candidate.Generation != state.ObservedHighWater &&
                    state.ObservedHighWater.HasValue && candidate.Generation < state.ObservedHighWater)
                {
                    accepted = candidate;
                    return false;
                }
            }

            // For Available(G): G must equal current ObservedHighWater
            if (token.Mode == OrganizationHierarchySafetyMode.Normal &&
                candidate.Generation != state.ObservedHighWater)
            {
                accepted = candidate;
                return false;
            }

            // For Quarantined: G must be above QuarantineFloor
            if (token.Mode == OrganizationHierarchySafetyMode.Quarantined &&
                candidate.Generation <= state.QuarantineFloor)
            {
                accepted = candidate;
                return false;
            }

            // Try to publish
            if (TryPublish(key, candidate))
            {
                // Release quarantine if eligible
                if (token.Mode == OrganizationHierarchySafetyMode.Quarantined &&
                    candidate.Generation == state.ObservedHighWater &&
                    candidate.Generation > state.QuarantineFloor)
                {
                    state.Update(OrganizationHierarchySafetyMode.Normal, state.ObservedHighWater, null, NextRevision());
                }

                accepted = candidate;
                return true;
            }

            accepted = candidate;
            return true; // publication failed but result is still valid (request-local)
        }
    }

    public bool TryCompleteUnavailableFallback(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchySnapshot requestLocalResult)
    {
        if (!_safetyRegistry.TryGetValue(token.ScopeKey, out var state))
            return false;

        lock (state.Gate)
        {
            // Must still be NORMAL with the same ObservedHighWater
            if (state.Mode != token.Mode)
                return false;
            if (state.ObservedHighWater != token.ObservedHighWater)
                return false;

            return true;
        }
    }

    public bool TryCompleteCacheFailureFallback(
        OrganizationHierarchyAdmissionToken token,
        OrganizationHierarchyCacheKey key,
        OrganizationHierarchySnapshot requestLocalResult)
    {
        return TryCompleteGenerationResult(token, key, requestLocalResult, out _);
    }

    public long GetPublicationGeneration(string tenantId)
    {
        var key = new OrganizationHierarchyCacheKey(tenantId, 0);
        // This is a simplification; in practice we'd track per-scope
        return 0;
    }

    private bool TryPublish(OrganizationHierarchyCacheKey key, OrganizationHierarchySnapshot candidate)
    {
        if (_snapshotCache.TryGetValue(key, out var existing) && existing is OrganizationHierarchySnapshot existingSnapshot)
        {
            if (existingSnapshot.Generation >= candidate.Generation)
                return false; // newer or equal already cached
        }

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(_options.SnapshotSlidingExpiration);

        _snapshotCache.Set(key, candidate, cacheEntryOptions);
        return true;
    }

    public void Dispose()
    {
        _snapshotCache.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
