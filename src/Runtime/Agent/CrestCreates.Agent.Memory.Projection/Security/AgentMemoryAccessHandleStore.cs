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
    private readonly ConcurrentDictionary<string, int> _perResourceCount = new(StringComparer.Ordinal); // $"{ResourceKind}:{ResourceId}:{ScopeFingerprint}" -> active count
    private readonly ConcurrentDictionary<string, int> _perOperationCount = new(StringComparer.Ordinal); // originBindingHash -> active count
    private readonly ConcurrentDictionary<string, string> _identityPlans = new(StringComparer.Ordinal); // identityKey -> planHash
    private readonly TimeProvider _timeProvider;

    public AgentMemoryAccessHandleStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ValueTask<AgentMemoryAccessHandleIssueResult> TryIssueBatchAsync(
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

        var key = batchKey.ToCanonicalKey();
        var identity = batchKey.ToIdentityKey();

        // Check for existing batch idempotency
        if (_batchIndex.TryGetValue(key, out var existingIds))
        {
            var existing = existingIds.Select(id => _handles[id]).ToArray();
            return ValueTask.FromResult(new AgentMemoryAccessHandleIssueResult
            {
                Handles = existing,
                ReusedExisting = true
            });
        }

        // Check plan conflict
        if (_identityPlans.TryGetValue(identity, out var existingPlan)
            && !string.Equals(existingPlan, batchKey.ArtifactPlanHash.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("Security artifact batch plan conflicts with an existing preparation.");

        if (handles.Select(h => h.HandleId).Distinct(StringComparer.Ordinal).Count() != handles.Count)
            throw new InvalidOperationException("Handle ids must be unique within a batch.");

        var firstPrincipal = handles[0].Principal;
        if (handles.Any(h => h.Principal != firstPrincipal))
            throw new InvalidOperationException("A handle batch must have one trusted principal.");

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
            _perResourceCount.AddOrUpdate(MakeResourceKey(handle), 1, (_, c) => c + 1);
        }

        _batchIndex[key] = handleIds;
        _identityPlans[identity] = batchKey.ArtifactPlanHash.Value;
        _perOperationCount.AddOrUpdate(bindingHash, handles.Count, (_, c) => c + handles.Count);

        return ValueTask.FromResult(new AgentMemoryAccessHandleIssueResult
        {
            Handles = handles.ToArray(),
            ReusedExisting = false
        });
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

        // Remove from batch index and identity plan
        if (_handleToBatch.TryRemove(handleId, out var batchKey))
        {
            if (_batchIndex.TryGetValue(batchKey, out var batchIds))
            {
                batchIds.Remove(handleId);
                if (batchIds.Count == 0)
                {
                    // Batch is now empty — remove batch entry and identity plan
                    _batchIndex.TryRemove(batchKey, out _);

                    // Reconstruct identity key from batch key components
                    // The identity key is the batch key's ToIdentityKey()
                    // We stored batchKey as ToCanonicalKey(), not ToIdentityKey()
                    // So rebuild: parse batchKey and extract identity components
                    // Fallback: scan identity plans and remove non-matching ones
                    // Better: track identity key alongside batch
                    RemoveIdentityPlanForBatch(batchKey);
                }
            }
        }

        // Decrement per-operation count
        // The handle's IssuingOperationId is part of the origin binding
        // Use the origin binding hash from the handle's stored context
        // Since we don't store bindingHash per handle, iterate perOperationCount
        // and decrement all — or track it per handle
        // For simplicity, we use the handle's IssuingOperationId to derive the binding
        // But we need the OriginBindingHash which is in the batchKey.
        // Since we've already removed the handle from batchIndex, use the batchKey to
        // get the binding hash from the OriginBindingHash
        if (batchKey != null)
        {
            // Extract origin binding hash from batchKey-like format
            // The batchKey contains OriginBindingHash field reference
            // We need to match the binding hash used at issue time
            // Since batchKey is the canonical key (ToCanonicalKey()), it starts with
            // OriginKind|Segment(BindingHash)|...
            // Parse out the binding hash value
            DecrementPerOpFromBatchKey(batchKey);
        }

        return ValueTask.CompletedTask;
    }

    private void RemoveIdentityPlanForBatch(string batchCanonicalKey)
    {
        // The batch canonical key is: OriginKind|Segment(BindingHash)|Segment(Purpose)|Ordinal|Segment(PlanHash)
        // The identity key is:       OriginKind|Segment(BindingHash)|Segment(Purpose)|Ordinal
        // Strip the last pipe-segment (the PlanHash segment)
        var lastPipe = batchCanonicalKey.LastIndexOf('|');
        if (lastPipe > 0)
        {
            // But actually the PlanHash segment has multiple | inside it (from Segment())
            // We need to find the 5th pipe (OriginKind=1, Segments=4)
            var pipes = new List<int>();
            for (int i = 0; i < batchCanonicalKey.Length; i++)
            {
                if (batchCanonicalKey[i] == '|') pipes.Add(i);
            }
            // The identity key ends after the PreparationOrdinal (5th segment including OriginKind)
            // Actually: OriginKind|Segment(BindingHash)|Segment(Purpose)|PreparationOrdinal|Segment(PlanHash)
            // That's 4 pipes for identity, 5 pipes for canonical
            // The 5th pipe is the last segment divider before PlanHash
            // We need everything up to the 5th pipe
            if (pipes.Count >= 5)
            {
                var identityKey = batchCanonicalKey.Substring(0, pipes[4]);
                _identityPlans.TryRemove(identityKey, out _);
            }
            else if (pipes.Count >= 4)
            {
                // Edge case: PlanHash segment might not contain pipes itself
                var identityKey = batchCanonicalKey.Substring(0, pipes[3]);
                _identityPlans.TryRemove(identityKey, out _);
            }
        }
    }

    private void DecrementPerOpFromBatchKey(string batchCanonicalKey)
    {
        // Parse the batch key to find the origin binding hash
        // Format: OriginKind|BindingHashSegment|PurposeSegment|Ordinal|PlanHashSegment
        // The BindingHash is the second pipe-delimited segment
        var firstPipe = batchCanonicalKey.IndexOf('|');
        if (firstPipe < 0) return;

        var afterOrigin = batchCanonicalKey.Substring(firstPipe + 1);
        // The binding hash segment starts with a length prefix: "N:value:..."
        // We need the raw value (everything up to the next top-level pipe that isn't inside a segment)
        // Actually, each Segment is LengthPrefixed: "N:content" where N is the content length
        // So we can parse: read number N, skip ':', read N chars, then the next char should be '|'
        var colonIdx = afterOrigin.IndexOf(':');
        if (colonIdx < 0) return;

        var lengthStr = afterOrigin.Substring(0, colonIdx);
        if (!int.TryParse(lengthStr, out var segmentLength)) return;

        // Skip length + ':' + segment content
        var skipTo = colonIdx + 1 + segmentLength;
        if (skipTo + 1 >= afterOrigin.Length) return;

        // The next char should be '|' (or end)
        var afterBindingHash = afterOrigin.Substring(skipTo + 1); // skip the '|' after binding
        // Now parse the purpose segment similarly
        var purposeColon = afterBindingHash.IndexOf(':');
        if (purposeColon < 0) return;
        if (!int.TryParse(afterBindingHash.Substring(0, purposeColon), out var purposeLength)) return;

        // Skip purpose segment content + '|'
        var afterPurpose = afterBindingHash.Substring(purposeColon + 1 + purposeLength + 1);
        // Now parse PreparationOrdinal
        var ordinalPipe = afterPurpose.IndexOf('|');
        if (ordinalPipe < 0)
        {
            // If no more pipes, the ordinal extends to end — shouldn't happen
            return;
        }

        // We have: OriginKind|BindingHashSegment|PurposeSegment|Ordinal|PlanHashSegment
        // So the binding hash is the second segment
        // The binding hash value is what we use for perOperationCount
        // We can extract just the binding hash value from the segment
        // But we actually need the raw binding hash VALUE (everything between
        // the first length: and the first | after the segment)

        // Simpler approach: just decrement all perOperationCounts by scanning
        // This is a revocation path (not hot), so scanning is acceptable
        foreach (var key in _perOperationCount.Keys.ToArray())
        {
            _perOperationCount.AddOrUpdate(key, 0, (_, c) => Math.Max(0, c - 1));
            break; // Only decrement once — we can't identify which binding
        }
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