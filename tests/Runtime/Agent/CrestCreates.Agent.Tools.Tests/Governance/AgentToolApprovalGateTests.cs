using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

public sealed class AgentToolApprovalGateTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        16,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task None_LowRiskReadOnly_ReturnsNotRequiredWithoutVerifier()
    {
        var gate = new FailClosedAgentToolApprovalGate();
        var context = GovernanceTestData.Context(
            approvalMode: AgentToolApprovalMode.None);

        var result = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = context
        });

        result.Decision.Should().Be(AgentToolApprovalDecision.NotRequired);
        result.ClaimState.Should().Be(AgentToolApprovalEvidenceClaimState.NotApplicable);
    }

    [Theory]
    [InlineData(CapabilityRiskLevel.High, AgentToolSideEffectKind.ReadOnly)]
    [InlineData(CapabilityRiskLevel.Low, AgentToolSideEffectKind.ExternalWrite)]
    public async Task ForcedFloor_CannotBeLoweredByNone(
        CapabilityRiskLevel risk,
        AgentToolSideEffectKind sideEffect)
    {
        var gate = new FailClosedAgentToolApprovalGate();
        var context = GovernanceTestData.Context(
            approvalMode: AgentToolApprovalMode.None,
            risk: risk,
            sideEffect: sideEffect);

        var result = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = context
        });

        result.Decision.Should().Be(AgentToolApprovalDecision.Denied);
        result.ReasonCode.Should().Be("approval_evidence_required");
    }

    [Fact]
    public async Task RequiredApproval_RejectsMalformedVerifierResult()
    {
        var gate = new FailClosedAgentToolApprovalGate(
            new StubVerifier(new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.Approved,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
            }));

        var result = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = GovernanceTestData.Context(),
            OpaqueEvidence = "opaque"
        });

        result.Decision.Should().Be(AgentToolApprovalDecision.Denied);
        result.ReasonCode.Should().Be("approval_evidence_rejected");
    }

    [Fact]
    public async Task PolicyDriven_VerifierMayProveApprovalIsNotRequired()
    {
        var gate = new FailClosedAgentToolApprovalGate(
            new StubVerifier(new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.NotRequired,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
            }));
        var context = GovernanceTestData.Context(
            approvalMode: AgentToolApprovalMode.PolicyDriven);

        var result = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = context
        });

        result.Decision.Should().Be(AgentToolApprovalDecision.NotRequired);
        result.ClaimState.Should().Be(AgentToolApprovalEvidenceClaimState.NotApplicable);
    }

    [Fact]
    public async Task SameEvidence_SameLogicalInvocationAndFingerprint_IsIdempotentAcrossAttempts()
    {
        var clock = new GovernanceTestData.MutableTimeProvider(Now);
        var verifier = new DevelopmentInMemoryAgentToolApprovalEvidenceVerifier(clock);
        var firstContext = GovernanceTestData.Context();
        verifier.Register(Evidence(
            "evidence-1",
            "opaque-1",
            firstContext));
        var gate = new FailClosedAgentToolApprovalGate(verifier);

        var first = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = firstContext,
            OpaqueEvidence = "opaque-1"
        });
        var retry = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = GovernanceTestData.Context(attemptId: "attempt-2"),
            OpaqueEvidence = "opaque-1"
        });

        first.Decision.Should().Be(AgentToolApprovalDecision.Approved);
        retry.Decision.Should().Be(AgentToolApprovalDecision.Approved);
        retry.EvidenceId.Should().Be(first.EvidenceId);
    }

    [Fact]
    public async Task SameEvidenceId_DifferentLogicalInvocations_IsAtomicallyClaimedOnce()
    {
        var clock = new GovernanceTestData.MutableTimeProvider(Now);
        var verifier = new DevelopmentInMemoryAgentToolApprovalEvidenceVerifier(clock);
        var firstContext = GovernanceTestData.Context();
        var secondContext = GovernanceTestData.Context(
            fingerprint: "fingerprint-2",
            invocationId: "invocation-2");
        verifier.Register(Evidence("shared-evidence", "opaque-1", firstContext));
        verifier.Register(Evidence("shared-evidence", "opaque-2", secondContext));
        var gate = new FailClosedAgentToolApprovalGate(verifier);
        using var barrier = new Barrier(3);

        var firstTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
            {
                Context = firstContext,
                OpaqueEvidence = "opaque-1"
            });
        });
        var secondTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
            {
                Context = secondContext,
                OpaqueEvidence = "opaque-2"
            });
        });

        barrier.SignalAndWait();
        var results = await Task.WhenAll(firstTask, secondTask);

        results.Count(result => result.Decision == AgentToolApprovalDecision.Approved)
            .Should().Be(1);
        results.Count(result => result.Decision == AgentToolApprovalDecision.Denied)
            .Should().Be(1);
    }

    [Fact]
    public async Task NewAttempt_RechecksExpiryAndRevocation()
    {
        var clock = new GovernanceTestData.MutableTimeProvider(Now);
        var verifier = new DevelopmentInMemoryAgentToolApprovalEvidenceVerifier(clock);
        var context = GovernanceTestData.Context();
        verifier.Register(Evidence("expiring", "expires", context, TimeSpan.FromMinutes(1)));
        verifier.Register(Evidence("revoked", "revokes", context, TimeSpan.FromHours(1)));
        var gate = new FailClosedAgentToolApprovalGate(verifier);

        (await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = context,
            OpaqueEvidence = "expires"
        })).Decision.Should().Be(AgentToolApprovalDecision.Approved);

        clock.Advance(TimeSpan.FromMinutes(2));
        var expired = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = GovernanceTestData.Context(attemptId: "attempt-2"),
            OpaqueEvidence = "expires"
        });
        verifier.Revoke("revoked").Should().BeTrue();
        var revoked = await gate.EvaluateAndClaimAsync(new AgentToolApprovalRequest
        {
            Context = GovernanceTestData.Context(attemptId: "attempt-3"),
            OpaqueEvidence = "revokes"
        });

        expired.Decision.Should().Be(AgentToolApprovalDecision.Denied);
        expired.ReasonCode.Should().Be("approval_evidence_expired");
        revoked.Decision.Should().Be(AgentToolApprovalDecision.Denied);
        revoked.ReasonCode.Should().Be("approval_evidence_revoked");
    }

    private static DevelopmentAgentToolApprovalEvidence Evidence(
        string evidenceId,
        string opaqueEvidence,
        AgentToolGovernanceContext context,
        TimeSpan? lifetime = null)
        => new()
        {
            EvidenceId = evidenceId,
            OpaqueEvidence = opaqueEvidence,
            LogicalInvocationKey = context.LogicalInvocationKey,
            InvocationFingerprint = context.InvocationFingerprint,
            ApproverReference = "approver-safe-reference",
            IssuedAt = Now.AddMinutes(-1),
            ExpiresAt = Now.Add(lifetime ?? TimeSpan.FromHours(1))
        };

    private sealed class StubVerifier(AgentToolApprovalResult result)
        : IAgentToolApprovalEvidenceVerifier
    {
        public ValueTask<AgentToolApprovalResult> VerifyAndClaimAsync(
            AgentToolApprovalRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(result);
    }
}
