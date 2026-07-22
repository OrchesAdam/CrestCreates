using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// In-memory handle store. Entries are lazily expired on read and intentionally
/// not evicted; durable providers must implement bounded retention and cleanup.
/// Revocation fully cleans batch index, identity plan, and per-operation counters.
/// </summary>
internal sealed class AgentMemoryAccessHandleStore : IAgentMemoryAccessHandleStore
{
    private readonly ConcurrentDictionary<string, AgentMemoryAccessResourceHandle> _handles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> _batchIndex = new(StringComparer.Ordinal); // batchKey -> handleIds
    private readonly ConcurrentDictionary<string, string> _handleToBatch = new(StringComparer.Ordinal); // handleId -> batchKey
    private readonly ConcurrentDictionary<string, string> _handleToBindingHash = new(StringComparer.Ordinal); // handleId -> originBindingHash value
    private readonly ConcurrentDictionary<string, int> _perResourceCount = new(StringComparer.Ordinal); // $"{ResourceKind}:{ResourceId}:{ScopeFingerprint}" -> active count
    private readonly ConcurrentDictionary<string, int> _perOperationCount = new(StringComparer.Ordinal); // originBindingHash -> active count
    private readonly ConcurrentDictionary<string, string> _identityPlans = new(StringComparer.Ordinal); // identityKey -> planHash
    private readonly ConcurrentDictionary<string, string> _batchToIdentity = new(StringComparer.Ordinal); // batchCanonicalKey -> identityKey
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _batchLocks = new();
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

        var batchLock = _batchLocks.GetOrAdd(batchKey.ToCanonicalKey(), _ => new SemaphoreSlim(1, 1));

        await batchLock.WaitAsync(cancellationToken);
        try
        {
            return TryIssueBatchInternal(batchKey, handles, maxActivePerResource, maxActivePerOperation);
        }
        finally
        {
            batchLock.Release();
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

        // Check for existing batch idempotency
        if (_batchIndex.TryGetValue(key, out var existingIds))
        {
            var existing = existingIds.Select(id => _handles[id]).ToArray();
            return new AgentMemoryAccessHandleIssueResult
            {
                Handles = existing,
                ReusedExisting = true
            };
        }

        // Check plan conflict
        if (_identityPlans.TryGetValue(identity, out var existingPlan)
            && !string.Equals(existingPlan, batchKey.ArtifactPlanHash.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("Security artifact batch plan conflicts with an existing preparation.");

        // Per-resource quota check
        foreach (var handle in handles)
        {
            var resourceKey = MakeResourceKey(handle);
            var active = _perResourceCount.GetValueOrDefault(resourceKey, 0);
            if (active + 1 > maxActivePerResource)
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
            _perResourceCount.AddOrUpdate(MakeResourceKey(handle), 1, (_, c) => c + 1);
        }

        _batchIndex[key] = handleIds;
        _identityPlans[identity] = batchKey.ArtifactPlanHash.Value;
        _batchToIdentity[key] = identity;
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

    public ValueTask RevokeAsync(
        string handleId,
        AgentMemoryCallerKind expectedCallerKind,
        CancellationToken cancellationToken = default)
    {
        if (!_handles.TryGetValue(handleId, out var handle))
            return ValueTask.CompletedTask;

        if (handle.Principal.CallerKind != expectedCallerKind)
            return ValueTask.CompletedTask;

        // Mark revoked
        _handles[handleId] = handle with { State = AgentMemorySecurityArtifactState.Revoked };

        // Decrement per-resource count
        _perResourceCount.AddOrUpdate(MakeResourceKey(handle), 0, (_, c) => Math.Max(0, c - 1));

        // Decrement per-operation count using stored binding hash
        if (_handleToBindingHash.TryRemove(handleId, out var bindingHash))
        {
            _perOperationCount.AddOrUpdate(bindingHash, 0, (_, c) => Math.Max(0, c - 1));
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

                    // Clean up identity plan using stored identity key
                    if (_batchToIdentity.TryRemove(batchCanonicalKey, out var identityKey))
                    {
                        _identityPlans.TryRemove(identityKey, out _);
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private static string MakeResourceKey(AgentMemoryAccessResourceHandle handle)
        => $"{handle.ResourceKind}:{handle.ResourceId}:{handle.ScopeFingerprint}";
}

/// <summary>
/// Computes a stable scope fingerprint for the projection access scope.
/// </summary>
internal static class AgentMemoryScopeFingerprint
{
    public static string Compute(AgentMemoryAccessScope scope)
    {
        var sb = new StringBuilder();
        sb.Append($"projection-scope-v1|{scope.TenantId}|{scope.AllowUnscopedMemory}|");
        var ordered = scope.VisibleDescriptorRefs
            .OrderBy(r => r.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Version);
        sb.Append(string.Join('|', ordered.Select(r => $"{r.Namespace}:{r.Id}:{r.Version}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}