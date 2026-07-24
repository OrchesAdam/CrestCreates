using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// In-memory handle store. Reads project expiry without mutation. An issuance
/// retry atomically replaces an incomplete, non-active, or expired batch after
/// cleaning its identity and quota accounting. Durable providers must also
/// implement bounded background retention.
/// </summary>
internal sealed class AgentMemoryAccessHandleStore : IAgentMemoryAccessHandleStore
{
    private readonly ConcurrentDictionary<string, AgentMemoryAccessResourceHandle> _handles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> _batchIndex = new(StringComparer.Ordinal); // batchKey -> handleIds
    private readonly ConcurrentDictionary<string, int> _batchExpectedCount = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _handleToBatch = new(StringComparer.Ordinal); // handleId -> batchKey
    private readonly ConcurrentDictionary<string, string> _handleToBindingHash = new(StringComparer.Ordinal); // handleId -> originBindingHash value
    private readonly ConcurrentDictionary<string, string> _handleToResourceKey = new(StringComparer.Ordinal); // handleId -> quota resource key
    private readonly ConcurrentDictionary<string, int> _perResourceCount = new(StringComparer.Ordinal); // $"{ResourceKind}:{ResourceId}:{ScopeFingerprint}" -> active count
    private readonly ConcurrentDictionary<string, int> _perOperationCount = new(StringComparer.Ordinal); // originBindingHash -> active count
    private readonly ConcurrentDictionary<string, string> _identityPlans = new(StringComparer.Ordinal); // identityKey -> planHash
    private readonly ConcurrentDictionary<string, string> _batchToIdentity = new(StringComparer.Ordinal); // batchCanonicalKey -> identityKey
    private readonly ConcurrentDictionary<string, string> _identityToBatch = new(StringComparer.Ordinal); // identityKey -> batchCanonicalKey
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly TimeProvider _timeProvider;

    public AgentMemoryAccessHandleStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async ValueTask<AgentMemoryAccessHandleIssueResult> TryIssueBatchAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        int maxActivePerResource,
        int maxActivePerOperation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchKey);
        ArgumentNullException.ThrowIfNull(handles);
        if (handles.Count == 0 || handles.Any(h => string.IsNullOrWhiteSpace(h.HandleId)))
            throw new InvalidOperationException("A non-empty opaque handle batch is required.");
        if (maxActivePerResource <= 0)
            throw new InvalidOperationException("Resource handle quota is exhausted.");
        if (maxActivePerOperation < handles.Count)
            throw new InvalidOperationException("Operation handle quota is exhausted.");
        if (handles.Select(h => h.HandleId).Distinct(StringComparer.Ordinal).Count() != handles.Count)
            throw new InvalidOperationException("Handle ids must be unique within a batch.");
        var firstPrincipal = handles[0].Principal;
        if (handles.Any(h => h.Principal != firstPrincipal))
            throw new InvalidOperationException("A handle batch must have one trusted principal.");

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            return TryIssueBatchInternal(batchKey, handles, maxActivePerResource, maxActivePerOperation);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private AgentMemoryAccessHandleIssueResult TryIssueBatchInternal(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        int maxActivePerResource,
        int maxActivePerOperation)
    {
        var key = batchKey.ToCanonicalKey();
        var identity = batchKey.ToIdentityKey();
        var now = _timeProvider.GetUtcNow();

        if (handles.Any(handle =>
                handle.State != AgentMemorySecurityArtifactState.Active
                || handle.ExpiresAt <= now))
        {
            throw new InvalidOperationException("Only active, unexpired resource handles can be issued.");
        }

        if (_batchIndex.TryGetValue(key, out var existingIds))
        {
            if (TryGetReusableBatch(key, existingIds, now, out var existing)
                && existing.Count == handles.Count)
            {
                return new AgentMemoryAccessHandleIssueResult
                {
                    Handles = existing,
                    ReusedExisting = true
                };
            }

            RemoveBatchInternal(key, existingIds);
        }
        else if (_identityToBatch.TryGetValue(identity, out var identityBatchKey))
        {
            if (_batchIndex.TryGetValue(identityBatchKey, out var identityBatchIds))
            {
                if (TryGetReusableBatch(identityBatchKey, identityBatchIds, now, out _))
                {
                    throw new InvalidOperationException(
                        "Security artifact batch plan conflicts with an existing preparation.");
                }

                RemoveBatchInternal(identityBatchKey, identityBatchIds);
            }
            else
            {
                RemoveBatchIdentityInternal(identityBatchKey, identity);
            }
        }

        if (_identityPlans.TryGetValue(identity, out var existingPlan)
            && !string.Equals(existingPlan, batchKey.ArtifactPlanHash.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("Security artifact batch plan conflicts with an existing preparation.");

        // Per-resource quota check — group by resource key to handle duplicates within batch
        var incomingByResource = handles
            .GroupBy(h => MakeResourceKey(h))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var (resourceKey, incomingCount) in incomingByResource)
        {
            var active = _perResourceCount.GetValueOrDefault(resourceKey, 0);
            if (active + incomingCount > maxActivePerResource)
                throw new InvalidOperationException("Active resource handle quota is exhausted.");
        }

        // Per-operation quota check
        var bindingHash = batchKey.OriginBindingHash.Value;
        var opActive = _perOperationCount.GetValueOrDefault(bindingHash, 0);
        if (opActive + handles.Count > maxActivePerOperation)
            throw new InvalidOperationException("Operation resource handle quota is exhausted.");

        // Store handles
        var handleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in handles)
        {
            _handles[handle.HandleId] = handle;
            handleIds.Add(handle.HandleId);
            _handleToBatch[handle.HandleId] = key;
            _handleToBindingHash[handle.HandleId] = bindingHash;
            var resourceKey = MakeResourceKey(handle);
            _handleToResourceKey[handle.HandleId] = resourceKey;
            _perResourceCount.AddOrUpdate(resourceKey, 1, (_, c) => c + 1);
        }

        _batchIndex[key] = handleIds;
        _batchExpectedCount[key] = handles.Count;
        _identityPlans[identity] = batchKey.ArtifactPlanHash.Value;
        _batchToIdentity[key] = identity;
        _identityToBatch[identity] = key;
        _perOperationCount.AddOrUpdate(bindingHash, handles.Count, (_, c) => c + handles.Count);

        return new AgentMemoryAccessHandleIssueResult
        {
            Handles = handles.ToArray(),
            ReusedExisting = false
        };
    }

    public ValueTask<AgentMemoryAccessResourceHandle?> GetAsync(
        string handleId,
        CancellationToken cancellationToken = default)
    {
        if (!_handles.TryGetValue(handleId, out var handle))
            return ValueTask.FromResult<AgentMemoryAccessResourceHandle?>(null);

        // Read-purification: return expired state view WITHOUT persisting
        if (handle.State == AgentMemorySecurityArtifactState.Active
            && handle.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            handle = handle with { State = AgentMemorySecurityArtifactState.Expired };
        }

        return ValueTask.FromResult<AgentMemoryAccessResourceHandle?>(handle);
    }

    public async ValueTask RevokeAsync(
        string handleId,
        AgentMemoryCallerKind expectedCallerKind,
        CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_handles.TryGetValue(handleId, out var handle))
                return;

            if (handle.Principal.CallerKind != expectedCallerKind)
                return;

            // Mark revoked
            _handles[handleId] = handle with { State = AgentMemorySecurityArtifactState.Revoked };

            if (_handleToResourceKey.TryRemove(handleId, out var resourceKey))
                DecrementCounter(_perResourceCount, resourceKey);

            // Decrement per-operation count using stored binding hash
            if (_handleToBindingHash.TryRemove(handleId, out var bindingHash))
            {
                DecrementCounter(_perOperationCount, bindingHash);
            }

            // Remove from batch index and identity plan
            if (_handleToBatch.TryRemove(handleId, out var batchCanonicalKey))
            {
                if (_batchIndex.TryGetValue(batchCanonicalKey, out var batchIds))
                {
                    batchIds.Remove(handleId);
                    if (batchIds.Count == 0)
                    {
                        _batchIndex.TryRemove(batchCanonicalKey, out _);
                        _batchExpectedCount.TryRemove(batchCanonicalKey, out _);

                        // Clean up identity plan using stored identity key
                        if (_batchToIdentity.TryRemove(batchCanonicalKey, out var identityKey))
                        {
                            _identityPlans.TryRemove(identityKey, out _);
                            _identityToBatch.TryRemove(identityKey, out _);
                        }
                    }
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private static string MakeResourceKey(AgentMemoryAccessResourceHandle handle)
        => $"{handle.ResourceKind}:{handle.ResourceId}:{handle.ScopeFingerprint}";

    private bool TryGetReusableBatch(
        string batchCanonicalKey,
        HashSet<string> existingIds,
        DateTimeOffset now,
        out IReadOnlyList<AgentMemoryAccessResourceHandle> existing)
    {
        if (!_batchExpectedCount.TryGetValue(batchCanonicalKey, out var expectedCount)
            || existingIds.Count != expectedCount)
        {
            existing = [];
            return false;
        }

        var artifacts = new List<AgentMemoryAccessResourceHandle>(existingIds.Count);
        foreach (var handleId in existingIds)
        {
            if (!_handles.TryGetValue(handleId, out var handle)
                || handle.State != AgentMemorySecurityArtifactState.Active
                || handle.ExpiresAt <= now)
            {
                existing = [];
                return false;
            }

            artifacts.Add(handle);
        }

        existing = artifacts;
        return true;
    }

    private void RemoveBatchInternal(string batchCanonicalKey, HashSet<string> handleIds)
    {
        foreach (var handleId in handleIds.ToArray())
        {
            _handles.TryRemove(handleId, out _);
            if (_handleToResourceKey.TryRemove(handleId, out var resourceKey))
                DecrementCounter(_perResourceCount, resourceKey);
            if (_handleToBindingHash.TryRemove(handleId, out var bindingHash))
                DecrementCounter(_perOperationCount, bindingHash);

            _handleToBatch.TryRemove(handleId, out _);
        }

        _batchIndex.TryRemove(batchCanonicalKey, out _);
        _batchExpectedCount.TryRemove(batchCanonicalKey, out _);
        if (_batchToIdentity.TryGetValue(batchCanonicalKey, out var identityKey))
            RemoveBatchIdentityInternal(batchCanonicalKey, identityKey);
    }

    private void RemoveBatchIdentityInternal(string batchCanonicalKey, string identityKey)
    {
        _batchToIdentity.TryRemove(batchCanonicalKey, out _);
        _identityToBatch.TryRemove(identityKey, out _);
        _identityPlans.TryRemove(identityKey, out _);
    }

    private static void DecrementCounter(
        ConcurrentDictionary<string, int> counters,
        string key)
    {
        if (!counters.TryGetValue(key, out var current))
            return;

        if (current <= 1)
            counters.TryRemove(key, out _);
        else
            counters[key] = current - 1;
    }
}
