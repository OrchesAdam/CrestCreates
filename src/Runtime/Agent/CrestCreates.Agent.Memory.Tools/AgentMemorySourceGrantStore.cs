namespace CrestCreates.Agent.Memory.Tools;

using CrestCreates.Agent.Memory.Abstractions;

/// <summary>
/// Development in-memory source-grant store. Entries are lazily marked
/// expired and are intentionally not evicted; durable providers must implement
/// bounded retention and expiry cleanup.
/// </summary>
public sealed class AgentMemorySourceGrantStore : IAgentMemorySourceGrantStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentMemorySourceGrant> _grants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _batches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _batchPlans = new(StringComparer.Ordinal);

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
        var key = batchKey.ToCanonicalKey();
        var identity = batchKey.ToIdentityKey();
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_batchPlans.TryGetValue(identity, out var existingPlan)
                && !string.Equals(existingPlan, batchKey.ArtifactPlanHash.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("Security artifact batch plan conflicts with an existing preparation.");
            if (_batches.TryGetValue(key, out var existingIds))
            {
                var existing = existingIds.Select(id => _grants[id]).ToArray();
                if (existing.Any(item => item.State != AgentMemorySecurityArtifactState.Active
                    || item.ExpiresAt <= now))
                    throw new AgentMemoryOperationException(
                        AgentMemoryOperationFailureCode.IdentityConflict,
                        "The security artifact batch was aborted or expired and cannot be reused.");
                return ValueTask.FromResult(new AgentMemoryGrantIssueResult
                {
                    Grants = existing,
                    ReusedExisting = true
                });
            }
            if (grants.Select(item => item.GrantId).Distinct(StringComparer.Ordinal).Count() != grants.Count)
                throw new InvalidOperationException("Grant ids must be unique within a batch.");
            var firstPrincipal = grants[0].Principal;
            if (grants.Any(item => item.Principal != firstPrincipal))
                throw new InvalidOperationException("A grant batch must have one trusted principal.");
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
            _batchPlans[identity] = batchKey.ArtifactPlanHash.Value;
            return ValueTask.FromResult(new AgentMemoryGrantIssueResult
            {
                Grants = grants.ToArray(),
                ReusedExisting = false
            });
        }
    }

    public ValueTask<AgentMemorySourceGrant?> GetAsync(string grantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
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
    }

    public ValueTask RevokeAsync(string grantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_grants.TryGetValue(grantId, out var grant))
                _grants[grantId] = grant with { State = AgentMemorySecurityArtifactState.Revoked };
        }
        return ValueTask.CompletedTask;
    }
}
