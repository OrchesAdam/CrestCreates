using System.Collections.Concurrent;

namespace CrestCreates.Agent.Memory.Tools;

public sealed class AgentMemorySourceGrantStore : IAgentMemorySourceGrantStore
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, AgentMemorySourceGrant> _grants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _batches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _batchPlans = new(StringComparer.Ordinal);

    public ValueTask<AgentMemoryGrantIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        int maxActiveGrantsPerResource,
        CancellationToken cancellationToken = default)
        => TryIssueBatchAsync(batchKey, grants, maxActiveGrantsPerResource, int.MaxValue, cancellationToken);

    public ValueTask<AgentMemoryGrantIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        int maxActiveGrantsPerResource,
        int maxActiveGrantsPerInvocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batchKey);
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Count == 0 || grants.Any(item => string.IsNullOrWhiteSpace(item.GrantId)))
            throw new InvalidOperationException("A non-empty opaque grant batch is required.");
        if (maxActiveGrantsPerResource <= 0 || maxActiveGrantsPerInvocation < grants.Count)
            throw new InvalidOperationException("Source grant quota is exhausted.");
        var key = CanonicalBatchKey(batchKey);
        var identity = BatchIdentity(batchKey);
        lock (_gate)
        {
            if (_batchPlans.TryGetValue(identity, out var existingPlan)
                && !string.Equals(existingPlan, batchKey.ArtifactPlanHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Security artifact batch plan conflicts with an existing preparation.");
            if (_batches.TryGetValue(key, out var existingIds))
                return ValueTask.FromResult(new AgentMemoryGrantIssueResult
                {
                    Grants = existingIds.Select(id => _grants[id]).ToArray(),
                    ReusedExisting = true
                });
            if (grants.Select(item => item.GrantId).Distinct(StringComparer.Ordinal).Count() != grants.Count)
                throw new InvalidOperationException("Grant ids must be unique within a batch.");
            var firstPrincipal = grants[0].Principal;
            if (grants.Any(item => item.Principal != firstPrincipal))
                throw new InvalidOperationException("A grant batch must have one trusted principal.");
            var now = DateTimeOffset.UtcNow;
            var requestedByResource = grants.GroupBy(item => (SourceKind: item.SourceRef.SourceKind, SourceId: item.SourceRef.SourceId, ScopeFingerprint: item.ScopeFingerprint));
            foreach (var group in requestedByResource)
            {
                if (group.Any(grant => _grants.ContainsKey(grant.GrantId)))
                    throw new InvalidOperationException("Source grant identity collision.");
                var active = _grants.Values.Count(item => item.SourceRef.SourceKind == group.Key.SourceKind
                    && item.SourceRef.SourceId == group.Key.SourceId
                    && item.Principal == firstPrincipal
                    && item.ScopeFingerprint == group.Key.ScopeFingerprint
                    && item.State == AgentMemorySecurityArtifactState.Active
                    && item.ExpiresAt > now);
                if (active + group.Count() > maxActiveGrantsPerResource)
                    throw new InvalidOperationException("Active source grant quota is exhausted.");
            }
            var invocationActive = _grants.Values.Count(item => item.Principal == firstPrincipal
                && item.State == AgentMemorySecurityArtifactState.Active
                && item.ExpiresAt > now);
            if (invocationActive + grants.Count > maxActiveGrantsPerInvocation)
                throw new InvalidOperationException("Invocation source grant quota is exhausted.");
            foreach (var grant in grants)
                _grants[grant.GrantId] = grant;
            _batches[key] = grants.Select(item => item.GrantId).ToArray();
            _batchPlans[identity] = batchKey.ArtifactPlanHash;
            return ValueTask.FromResult(new AgentMemoryGrantIssueResult
            {
                Grants = grants.ToArray(),
                ReusedExisting = false
            });
        }
    }

    public ValueTask<AgentMemorySourceGrant?> GetAsync(string grantId, CancellationToken cancellationToken = default)
    {
        if (!_grants.TryGetValue(grantId, out var grant))
            return ValueTask.FromResult<AgentMemorySourceGrant?>(null);
        if (grant.State == AgentMemorySecurityArtifactState.Active && grant.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            grant = grant with { State = AgentMemorySecurityArtifactState.Expired };
            _grants[grantId] = grant;
        }
        return ValueTask.FromResult<AgentMemorySourceGrant?>(grant);
    }

    public ValueTask RevokeAsync(string grantId, CancellationToken cancellationToken = default)
    {
        if (_grants.TryGetValue(grantId, out var grant))
            _grants[grantId] = grant with { State = AgentMemorySecurityArtifactState.Revoked };
        return ValueTask.CompletedTask;
    }

    private static string CanonicalBatchKey(AgentMemorySecurityArtifactBatchKey key)
        => string.Join("|", key.OriginKind, key.LogicalInvocationKeyHash, key.InvocationFingerprint,
            key.ArtifactPurpose, key.PreparationOrdinal, key.ArtifactPlanHash);

    private static string BatchIdentity(AgentMemorySecurityArtifactBatchKey key)
        => string.Join("|", key.OriginKind, key.LogicalInvocationKeyHash, key.InvocationFingerprint,
            key.ArtifactPurpose, key.PreparationOrdinal);
}
