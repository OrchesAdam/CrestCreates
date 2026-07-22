using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// In-memory source grant store. Same read-purification semantics as HandleStore.
/// Revocation fully cleans batch index, identity plan, and per-operation counters.
/// </summary>
internal sealed class AgentMemoryAccessGrantStore : IAgentMemoryAccessGrantStore
{
    private readonly ConcurrentDictionary<string, AgentMemoryAccessSourceGrant> _grants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> _batchIndex = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _grantToBatch = new(StringComparer.Ordinal); // grantId -> batchKey
    private readonly ConcurrentDictionary<string, string> _grantToBindingHash = new(StringComparer.Ordinal); // grantId -> originBindingHash value
    private readonly ConcurrentDictionary<string, int> _perResourceCount = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _perOperationCount = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _identityPlans = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _batchToIdentity = new(StringComparer.Ordinal); // batchCanonicalKey -> identityKey
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly TimeProvider _timeProvider;

    public AgentMemoryAccessGrantStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async ValueTask<AgentMemoryAccessGrantIssueResult> TryIssueBatchAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        int maxActivePerResource,
        int maxActivePerOperation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchKey);
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Count == 0 || grants.Any(g => string.IsNullOrWhiteSpace(g.GrantId)))
            throw new InvalidOperationException("A non-empty opaque grant batch is required.");
        if (maxActivePerResource <= 0)
            throw new InvalidOperationException("Source grant quota is exhausted.");
        if (maxActivePerOperation < grants.Count)
            throw new InvalidOperationException("Operation grant quota is exhausted.");
        if (grants.Select(g => g.GrantId).Distinct(StringComparer.Ordinal).Count() != grants.Count)
            throw new InvalidOperationException("Grant ids must be unique within a batch.");
        var firstPrincipal = grants[0].Principal;
        if (grants.Any(g => g.Principal != firstPrincipal))
            throw new InvalidOperationException("A grant batch must have one trusted principal.");

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            return TryIssueBatchInternal(batchKey, grants, maxActivePerResource, maxActivePerOperation);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private AgentMemoryAccessGrantIssueResult TryIssueBatchInternal(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        int maxActivePerResource,
        int maxActivePerOperation)
    {
        var key = batchKey.ToCanonicalKey();
        var identity = batchKey.ToIdentityKey();

        if (_batchIndex.TryGetValue(key, out var existingIds))
        {
            var existing = existingIds.Select(id => _grants[id]).ToArray();
            return new AgentMemoryAccessGrantIssueResult
            {
                Grants = existing,
                ReusedExisting = true
            };
        }

        if (_identityPlans.TryGetValue(identity, out var existingPlan)
            && !string.Equals(existingPlan, batchKey.ArtifactPlanHash.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("Security artifact batch plan conflicts with an existing preparation.");

        foreach (var grant in grants)
        {
            var resourceKey = $"{grant.SourceRef.SourceKind}:{grant.SourceRef.SourceId}:{grant.ScopeFingerprint}";
            var active = _perResourceCount.GetValueOrDefault(resourceKey, 0);
            if (active + 1 > maxActivePerResource)
                throw new InvalidOperationException("Active source grant quota is exhausted.");
        }

        var bindingHash = batchKey.OriginBindingHash.Value;
        var opActive = _perOperationCount.GetValueOrDefault(bindingHash, 0);
        if (opActive + grants.Count > maxActivePerOperation)
            throw new InvalidOperationException("Operation source grant quota is exhausted.");

        var grantIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            _grants[grant.GrantId] = grant;
            grantIds.Add(grant.GrantId);
            _grantToBatch[grant.GrantId] = key;
            _grantToBindingHash[grant.GrantId] = bindingHash;
            var resourceKey = $"{grant.SourceRef.SourceKind}:{grant.SourceRef.SourceId}:{grant.ScopeFingerprint}";
            _perResourceCount.AddOrUpdate(resourceKey, 1, (_, c) => c + 1);
        }

        _batchIndex[key] = grantIds;
        _identityPlans[identity] = batchKey.ArtifactPlanHash.Value;
        _batchToIdentity[key] = identity;
        _perOperationCount.AddOrUpdate(bindingHash, grants.Count, (_, c) => c + grants.Count);

        return new AgentMemoryAccessGrantIssueResult
        {
            Grants = grants.ToArray(),
            ReusedExisting = false
        };
    }

    public ValueTask<AgentMemoryAccessSourceGrant?> GetAsync(
        string grantId,
        CancellationToken cancellationToken = default)
    {
        if (!_grants.TryGetValue(grantId, out var grant))
            return ValueTask.FromResult<AgentMemoryAccessSourceGrant?>(null);

        if (grant.State == AgentMemorySecurityArtifactState.Active
            && grant.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            grant = grant with { State = AgentMemorySecurityArtifactState.Expired };
        }

        return ValueTask.FromResult<AgentMemoryAccessSourceGrant?>(grant);
    }

    public async ValueTask RevokeAsync(
        string grantId,
        AgentMemoryCallerKind expectedCallerKind,
        CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_grants.TryGetValue(grantId, out var grant))
                return;

            if (grant.Principal.CallerKind != expectedCallerKind)
                return;

            // Mark revoked
            _grants[grantId] = grant with { State = AgentMemorySecurityArtifactState.Revoked };

            // Decrement per-resource count
            var resourceKey = $"{grant.SourceRef.SourceKind}:{grant.SourceRef.SourceId}:{grant.ScopeFingerprint}";
            _perResourceCount.AddOrUpdate(resourceKey, 0, (_, c) => Math.Max(0, c - 1));

            // Decrement per-operation count using stored binding hash
            if (_grantToBindingHash.TryRemove(grantId, out var bindingHash))
            {
                _perOperationCount.AddOrUpdate(bindingHash, 0, (_, c) => Math.Max(0, c - 1));
            }

            // Remove from batch index and identity plan
            if (_grantToBatch.TryRemove(grantId, out var batchCanonicalKey))
            {
                if (_batchIndex.TryGetValue(batchCanonicalKey, out var batchIds))
                {
                    batchIds.Remove(grantId);
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
        }
        finally
        {
            _stateLock.Release();
        }
    }
}