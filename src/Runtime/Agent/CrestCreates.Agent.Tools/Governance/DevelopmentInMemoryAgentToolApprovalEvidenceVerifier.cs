using System.Collections.Concurrent;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Approval evidence registered explicitly for the volatile development
/// verifier. The opaque token is never returned in an approval result.
/// </summary>
public sealed record DevelopmentAgentToolApprovalEvidence
{
    public required string EvidenceId { get; init; }

    public required string OpaqueEvidence { get; init; }

    public required AgentToolLogicalInvocationKey LogicalInvocationKey { get; init; }

    public required string InvocationFingerprint { get; init; }

    public required string ApproverReference { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Volatile single-process approval verifier intended only for development and
/// tests. Claims and revocations are lost on restart and are not atomic across
/// nodes; production Hosts must supply a durable verifier.
/// </summary>
public sealed class DevelopmentInMemoryAgentToolApprovalEvidenceVerifier
    : IAgentToolApprovalEvidenceVerifier
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, DevelopmentAgentToolApprovalEvidence> _evidenceByToken
        = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EvidenceClaim> _claimsByEvidenceId
        = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _revokedEvidenceIds
        = new(StringComparer.Ordinal);

    public DevelopmentInMemoryAgentToolApprovalEvidenceVerifier(
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Register(DevelopmentAgentToolApprovalEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidateEvidence(evidence);

        if (!_evidenceByToken.TryAdd(evidence.OpaqueEvidence, evidence))
        {
            throw new InvalidOperationException(
                "The opaque approval evidence token is already registered.");
        }
    }

    public bool Revoke(string evidenceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        return _revokedEvidenceIds.TryAdd(evidenceId, 0);
    }

    public ValueTask<AgentToolApprovalResult> VerifyAndClaimAsync(
        AgentToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AgentToolGovernanceGuard.IsValid(request.Context)
            || string.IsNullOrWhiteSpace(request.OpaqueEvidence)
            || !_evidenceByToken.TryGetValue(request.OpaqueEvidence, out var evidence))
        {
            return ValueTask.FromResult(Rejected("approval_evidence_unknown"));
        }

        var now = _timeProvider.GetUtcNow();
        if (evidence.IssuedAt > now || evidence.ExpiresAt <= now)
        {
            return ValueTask.FromResult(Rejected("approval_evidence_expired"));
        }

        if (_revokedEvidenceIds.ContainsKey(evidence.EvidenceId))
        {
            return ValueTask.FromResult(Rejected("approval_evidence_revoked"));
        }

        if (evidence.LogicalInvocationKey != request.Context.LogicalInvocationKey
            || !string.Equals(
                evidence.InvocationFingerprint,
                request.Context.InvocationFingerprint,
                StringComparison.Ordinal))
        {
            return ValueTask.FromResult(Rejected("approval_evidence_binding_mismatch"));
        }

        var requestedClaim = new EvidenceClaim(
            evidence.LogicalInvocationKey,
            evidence.InvocationFingerprint);
        var persistedClaim = _claimsByEvidenceId.GetOrAdd(
            evidence.EvidenceId,
            requestedClaim);

        if (persistedClaim != requestedClaim)
        {
            return ValueTask.FromResult(Rejected("approval_evidence_already_claimed"));
        }

        // Recheck revocation after the atomic claim so a concurrently revoked
        // evidence never returns a positive decision after revocation is visible.
        if (_revokedEvidenceIds.ContainsKey(evidence.EvidenceId))
        {
            return ValueTask.FromResult(Rejected("approval_evidence_revoked"));
        }

        return ValueTask.FromResult(new AgentToolApprovalResult
        {
            Decision = AgentToolApprovalDecision.Approved,
            ClaimState = AgentToolApprovalEvidenceClaimState.Claimed,
            EvidenceId = evidence.EvidenceId,
            ApproverReference = evidence.ApproverReference,
            ReasonCode = "approval_evidence_verified"
        });
    }

    private static void ValidateEvidence(DevelopmentAgentToolApprovalEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.EvidenceId)
            || string.IsNullOrWhiteSpace(evidence.OpaqueEvidence)
            || string.IsNullOrWhiteSpace(evidence.InvocationFingerprint)
            || string.IsNullOrWhiteSpace(evidence.ApproverReference)
            || !AgentToolGovernanceGuard.IsValid(evidence.LogicalInvocationKey)
            || evidence.ExpiresAt <= evidence.IssuedAt)
        {
            throw new ArgumentException(
                "Development approval evidence has an invalid contract.",
                nameof(evidence));
        }
    }

    private static AgentToolApprovalResult Rejected(string reasonCode)
        => new()
        {
            Decision = AgentToolApprovalDecision.Denied,
            ClaimState = AgentToolApprovalEvidenceClaimState.Rejected,
            ReasonCode = reasonCode
        };

    private sealed record EvidenceClaim(
        AgentToolLogicalInvocationKey LogicalInvocationKey,
        string InvocationFingerprint);
}
