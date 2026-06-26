using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

// semantic-string-guard: allow

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Phase C tests for the activation review orchestration workflow:
/// IActivationReviewOrchestrator, DescriptorActivationReviewHumanTaskEventHandler,
/// and ToolService integration.
/// </summary>
public class ActivationReviewOrchestratorTests : AgentControlPlaneTestBase
{
    // ════════════════════════════════════════════════════════════════════════
    // Helper factories
    // ════════════════════════════════════════════════════════════════════════

    private static CanonicalHash CreateCanonicalHash(string value)
        => new()
        {
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = CanonicalHashArtifactNames.Descriptor,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = CanonicalHashPurposeNames.Contract,
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "v1",
            Value = value
        };

    private static BindingHashes CreateBindingHashes()
        => new()
        {
            SourceReviewHash = CreateCanonicalHash("src-review-hash"),
            ManifestHash = CreateCanonicalHash("manifest-hash"),
            EvidenceHash = CreateCanonicalHash("evidence-hash"),
            EnvelopeHash = CreateCanonicalHash("envelope-hash"),
            ContractHash = CreateCanonicalHash("contract-hash"),
            DefinitionHash = CreateCanonicalHash("definition-hash")
        };

    private static ActivationBindingSnapshot CreateBindingSnapshot(string draftId = "draft-001")
        => new()
        {
            TenantId = TestTenantId,
            DraftId = draftId,
            DraftVersion = 1,
            ReviewResultId = "review-001",
            PackagePreviewId = "pkg-001",
            EvidencePreviewId = "ev-001",
            Hashes = CreateBindingHashes(),
            CorrelationId = TestCorrelationId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static ActivationRequest CreateActivationRequest(
        string requestId = "req-001",
        ActivationRequestStatus status = ActivationRequestStatus.UnderReview,
        DescriptorActivationEligibility eligibility = DescriptorActivationEligibility.RequiresHumanReview,
        string draftId = "draft-001")
        => new()
        {
            RequestId = requestId,
            TenantId = TestTenantId,
            DraftId = draftId,
            Status = status,
            SubmittedAt = DateTimeOffset.UtcNow,
            SubmittedBy = TestActorId,
            CreatedByActorId = TestActorId,
            CreatedByActorKind = DescriptorActivationActorKind.Agent,
            GovernanceDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            Eligibility = eligibility,
            BindingSnapshot = CreateBindingSnapshot(draftId),
            Diagnostics = []
        };

    private static DescriptorActivationReviewDecision CreateReviewDecision(
        string requestId = "req-001",
        DescriptorActivationReviewOutcome outcome = DescriptorActivationReviewOutcome.Approved,
        string actorId = "reviewer-001",
        DescriptorActivationActorKind actorKind = DescriptorActivationActorKind.Human)
        => new()
        {
            ActivationRequestId = requestId,
            TenantId = TestTenantId,
            CorrelationId = TestCorrelationId,
            Decision = outcome,
            ActorKind = actorKind,
            ActorId = actorId,
            Reason = outcome == DescriptorActivationReviewOutcome.Approved ? "Approved" : "Rejected",
            DecidedAt = DateTimeOffset.UtcNow,
            BoundEvidenceHash = CreateCanonicalHash("evidence-hash"),
            BoundEnvelopeHash = CreateCanonicalHash("envelope-hash")
        };

    private DefaultActivationReviewOrchestrator CreateOrchestrator(
        Mock<IDescriptorActivationRequestService>? activationServiceMock = null)
    {
        return new DefaultActivationReviewOrchestrator(
            HumanTaskRuntimeMock.Object,
            (activationServiceMock ?? ActivationRequestServiceMock).Object,
            NullLogger<DefaultActivationReviewOrchestrator>.Instance);
    }

    private readonly Mock<IHumanTaskInstanceStore> HumanTaskInstanceStoreMock = new();

    private DescriptorActivationReviewHumanTaskEventHandler CreateEventHandler(
        IActivationReviewOrchestrator? orchestrator = null)
    {
        return new DescriptorActivationReviewHumanTaskEventHandler(
            orchestrator ?? ActivationReviewOrchestratorMock.Object,
            HumanTaskInstanceStoreMock.Object,
            NullLogger<DescriptorActivationReviewHumanTaskEventHandler>.Instance);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 1: CreateActivationReviewTaskAsync — RequiresHumanReview → creates HumanTask
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateActivationReviewTaskAsync_RequiresHumanReview_CreatesHumanTask()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var context = CreateContext("SubmitActivationRequest");
        var activationRequest = CreateActivationRequest(
            eligibility: DescriptorActivationEligibility.RequiresHumanReview);

        HumanTaskRuntimeMock
            .Setup(x => x.CreateAsync(It.IsAny<HumanTaskCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskInstance
            {
                Id = "task-instance-001",
                HumanTaskId = "descriptor-activation-review",
                Status = HumanTaskInstanceStatus.Created,
                TenantId = TestTenantId,
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var policy = new DescriptorActivationPolicy
        {
            RequireHumanReviewForAll = false,
            ForbidSelfApproval = true,
            AutoActivateAllowedWhenPolicyPermits = true
        };
        var result = await orchestrator.CreateActivationReviewTaskAsync(context, activationRequest, policy);

        // Assert
        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().Be("task-instance-001");

        HumanTaskRuntimeMock.Verify(
            x => x.CreateAsync(
                It.Is<HumanTaskCreationRequest>(r =>
                    r.HumanTaskId == "descriptor-activation-review" &&
                    r.TenantId == TestTenantId &&
                    r.Input is DescriptorActivationReviewTaskInput),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 2: CreateActivationReviewTaskAsync — AutoActivatable → returns error
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateActivationReviewTaskAsync_AutoActivatable_ReturnsError()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var context = CreateContext("SubmitActivationRequest");
        var activationRequest = CreateActivationRequest(
            eligibility: DescriptorActivationEligibility.AutoActivatable);

        // Act
        var policy = new DescriptorActivationPolicy
        {
            RequireHumanReviewForAll = false,
            ForbidSelfApproval = true,
            AutoActivateAllowedWhenPolicyPermits = true
        };
        var result = await orchestrator.CreateActivationReviewTaskAsync(context, activationRequest, policy);

        // Assert
        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "ACTIVATION_REVIEW_NOT_REQUIRED");

        HumanTaskRuntimeMock.Verify(
            x => x.CreateAsync(It.IsAny<HumanTaskCreationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 3: ProcessReviewDecisionAsync — Approved → calls Approve (gate now internal)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessReviewDecisionAsync_Approved_CallsApproveOnly()
    {
        // Arrange
        var activationServiceMock = new Mock<IDescriptorActivationRequestService>();
        var orchestrator = CreateOrchestrator(activationServiceMock);
        var reviewDecision = CreateReviewDecision(outcome: DescriptorActivationReviewOutcome.Approved);
        var activatedRequest = CreateActivationRequest(
            requestId: "req-001",
            status: ActivationRequestStatus.Activated);

        activationServiceMock
            .Setup(x => x.ApproveActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                "req-001",
                reviewDecision,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<ActivationRequest>.Success(activatedRequest));

        // Act
        await orchestrator.ProcessReviewDecisionAsync(reviewDecision);

        // Assert
        activationServiceMock.Verify(
            x => x.ApproveActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                "req-001",
                reviewDecision,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // ExecuteActivationGateAsync should NOT be called — gate is now internal to ApproveActivationRequestAsync
        activationServiceMock.Verify(
            x => x.ExecuteActivationGateAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 4: ProcessReviewDecisionAsync — Rejected → calls RejectActivationRequestAsync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessReviewDecisionAsync_Rejected_CallsReject()
    {
        // Arrange
        var activationServiceMock = new Mock<IDescriptorActivationRequestService>();
        var orchestrator = CreateOrchestrator(activationServiceMock);
        var reviewDecision = CreateReviewDecision(outcome: DescriptorActivationReviewOutcome.Rejected);
        var rejectedRequest = CreateActivationRequest(
            requestId: "req-001",
            status: ActivationRequestStatus.Rejected);

        activationServiceMock
            .Setup(x => x.RejectActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                "req-001",
                reviewDecision,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<ActivationRequest>.Success(rejectedRequest));

        // Act
        await orchestrator.ProcessReviewDecisionAsync(reviewDecision);

        // Assert
        activationServiceMock.Verify(
            x => x.RejectActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                "req-001",
                reviewDecision,
                It.IsAny<CancellationToken>()),
            Times.Once);

        activationServiceMock.Verify(
            x => x.ApproveActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<string>(),
                It.IsAny<DescriptorActivationReviewDecision>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        activationServiceMock.Verify(
            x => x.ExecuteActivationGateAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 5: EventHandler — activation review task → processes decision
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EventHandler_ActivationReviewTask_ProcessesDecision()
    {
        // Arrange
        var orchestratorMock = new Mock<IActivationReviewOrchestrator>();
        var handler = CreateEventHandler(orchestratorMock.Object);
        var reviewDecision = CreateReviewDecision();

        var completedEvent = new HumanTaskCompletedEvent
        {
            HumanTaskId = "descriptor-activation-review",
            HumanTaskInstanceId = "task-instance-001",
            Outcome = "Approved",
            Result = reviewDecision
        };

        // Act
        await handler.HandleAsync(completedEvent);

        // Assert
        orchestratorMock.Verify(
            x => x.ProcessReviewDecisionAsync(
                It.Is<DescriptorActivationReviewDecision>(d =>
                    d.ActivationRequestId == "req-001" &&
                    d.Decision == DescriptorActivationReviewOutcome.Approved),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 6: EventHandler — non-activation task → ignores
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EventHandler_NonActivationTask_Ignores()
    {
        // Arrange
        var orchestratorMock = new Mock<IActivationReviewOrchestrator>();
        var handler = CreateEventHandler(orchestratorMock.Object);

        var completedEvent = new HumanTaskCompletedEvent
        {
            HumanTaskId = "some-other-task",
            HumanTaskInstanceId = "task-instance-002",
            Outcome = "Completed"
        };

        // Act
        await handler.HandleAsync(completedEvent);

        // Assert
        orchestratorMock.Verify(
            x => x.ProcessReviewDecisionAsync(
                It.IsAny<DescriptorActivationReviewDecision>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 7: EventHandler — invalid result → logs error, does not call orchestrator
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EventHandler_InvalidResult_LogsErrorAndDoesNotCallOrchestrator()
    {
        // Arrange
        var orchestratorMock = new Mock<IActivationReviewOrchestrator>();
        var handler = CreateEventHandler(orchestratorMock.Object);

        var completedEvent = new HumanTaskCompletedEvent
        {
            HumanTaskId = "descriptor-activation-review",
            HumanTaskInstanceId = "task-instance-003",
            Outcome = "Completed",
            Result = "not a valid review decision"
        };

        // Act
        await handler.HandleAsync(completedEvent);

        // Assert
        orchestratorMock.Verify(
            x => x.ProcessReviewDecisionAsync(
                It.IsAny<DescriptorActivationReviewDecision>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 8: SubmitActivationRequest with UnderReview result → creates review task
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SubmitActivationRequest_UnderReview_CreatesReviewTask()
    {
        // Arrange — override ActivationRequestServiceMock BEFORE creating service
        var underReviewRequest = CreateActivationRequest(
            requestId: "req-under-review",
            status: ActivationRequestStatus.UnderReview,
            eligibility: DescriptorActivationEligibility.RequiresHumanReview);

        ActivationRequestServiceMock
            .Setup(x => x.CreateActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<SubmitActivationRequestRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<ActivationRequest>.Success(underReviewRequest));

        ActivationReviewOrchestratorMock
            .Setup(x => x.CreateActivationReviewTaskAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<ActivationRequest>(),
                It.IsAny<DescriptorActivationPolicy>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<string>.Success("task-instance-created"));

        SetupTopologySnapshot();

        // Create service with all binding artifacts pre-populated
        // (EnsureActivationRequestServiceSetup will override our mock, re-override below)
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) =
            await CreateServiceWithFullBindingArtifacts();

        // Re-override the mock since EnsureActivationRequestServiceSetup was called during CreateService
        ActivationRequestServiceMock
            .Setup(x => x.CreateActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<SubmitActivationRequestRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<ActivationRequest>.Success(underReviewRequest));

        var context = CreateContext("SubmitActivationRequest");

        var bindingSnapshot = CreateBindingSnapshot() with
        {
            ReviewResultId = reviewResultId,
            PackagePreviewId = packagePreviewId,
            EvidencePreviewId = evidencePreviewId
        };

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = bindingSnapshot
        };

        // Act
        var result = await service.SubmitActivationRequestAsync(context, request);

        // Assert
        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.Status.Should().Be(ActivationRequestStatus.UnderReview,
            $"expected UnderReview but got {result.Value!.Status}");

        ActivationReviewOrchestratorMock.Verify(
            x => x.CreateActivationReviewTaskAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.Is<ActivationRequest>(r => r.Status == ActivationRequestStatus.UnderReview),
                It.IsAny<DescriptorActivationPolicy>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
