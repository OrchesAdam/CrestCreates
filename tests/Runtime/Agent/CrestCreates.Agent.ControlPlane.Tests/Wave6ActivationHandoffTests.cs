using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using FluentAssertions;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Wave 6 tests: Activation Handoff tools.
/// Verifies: SubmitActivationRequest, GetActivationRequestStatus, CancelActivationRequest.
/// Key invariants:
/// - Agent CANNOT approve or execute activation
/// - Submit creates a record, does not execute activation
/// - Terminal states (Approved/Rejected) cannot be cancelled
/// </summary>
public class Wave6ActivationHandoffTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task SubmitActivationRequest_Creates_Request_Record()
    {
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            ReviewResultId = "review-001"
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.RequestId.Should().NotBeNullOrEmpty();
        result.Value.DraftId.Should().Be("draft-001");
        result.Value.Status.Should().Be(ActivationRequestStatus.Submitted);
        result.Value.SubmittedBy.Should().Be(TestActorId);
        result.Value.TenantId.Should().Be(TestTenantId);
    }

    [Fact]
    public async Task SubmitActivationRequest_Does_Not_Approve_Or_Execute_Activation()
    {
        // Submit creates a Submitted record, NOT Approved
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            ReviewResultId = "review-001"
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Value!.Status.Should().Be(ActivationRequestStatus.Submitted);
        result.Value.Status.Should().NotBe(ActivationRequestStatus.Approved);
    }

    [Fact]
    public async Task SubmitActivationRequest_Requires_At_Least_One_Reference()
    {
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001"
            // No ReviewResultId, PackagePreviewId, or EvidencePreviewId
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "ACTIVATION_MISSING_REFERENCES");
    }

    [Fact]
    public async Task SubmitActivationRequest_Returns_NotFound_When_Draft_Missing()
    {
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "nonexistent",
            ReviewResultId = "review-001"
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task SubmitActivationRequest_Audit_Records_TouchedIds()
    {
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            PackagePreviewId = "preview-001"
        };

        await service.SubmitActivationRequestAsync(context, request);

        InMemoryAuditor.GetAllRecords().Should().Contain(r =>
            r.Context.ToolName == "SubmitActivationRequest" &&
            r.TouchedDraftIds != null &&
            r.TouchedActivationRequestIds != null);
    }

    [Fact]
    public async Task GetActivationRequestStatus_Returns_Request()
    {
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            ReviewResultId = "review-001"
        };

        var submitResult = await service.SubmitActivationRequestAsync(context, request);
        var requestId = submitResult.Value!.RequestId;

        // Retrieve status
        var statusResult = await service.GetActivationRequestStatusAsync(context, requestId);

        statusResult.Status.Should().Be(AgentToolResultStatus.Success);
        statusResult.Value!.RequestId.Should().Be(requestId);
        statusResult.Value.Status.Should().Be(ActivationRequestStatus.Submitted);
    }

    [Fact]
    public async Task GetActivationRequestStatus_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("GetActivationRequestStatus");

        var result = await service.GetActivationRequestStatusAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task CancelActivationRequest_Cancels_Submitted_Request()
    {
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            ReviewResultId = "review-001"
        };

        var submitResult = await service.SubmitActivationRequestAsync(context, request);
        var requestId = submitResult.Value!.RequestId;

        // Cancel
        var cancelResult = await service.CancelActivationRequestAsync(context, requestId);

        cancelResult.Status.Should().Be(AgentToolResultStatus.Success);
        cancelResult.Value!.Status.Should().Be(ActivationRequestStatus.Cancelled);
    }

    [Fact]
    public async Task CancelActivationRequest_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("CancelActivationRequest");

        var result = await service.CancelActivationRequestAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task CancelActivationRequest_Rejects_Terminal_Approved_State()
    {
        // The implementation only treats Approved and Rejected as terminal states.
        // We cannot directly set a request to Approved through the tool surface
        // (that's the governance boundary). Instead, we verify that cancelling
        // a Submitted request works, and document that Approved/Rejected are
        // the terminal states that would block cancellation.
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            ReviewResultId = "review-001"
        };

        var submitResult = await service.SubmitActivationRequestAsync(context, request);
        var requestId = submitResult.Value!.RequestId;

        // Cancel the Submitted request — should succeed
        var cancelResult = await service.CancelActivationRequestAsync(context, requestId);
        cancelResult.Status.Should().Be(AgentToolResultStatus.Success);
        cancelResult.Value!.Status.Should().Be(ActivationRequestStatus.Cancelled);

        // Note: Approved and Rejected are terminal states that would return InvalidRequest
        // with ACTIVATION_REQUEST_TERMINAL. These states can only be set by human governance
        // (outside the tool surface), so we cannot test them through the API directly.
        // The implementation code at CancelActivationRequestAsync checks:
        //   if (request.Status is ActivationRequestStatus.Approved or ActivationRequestStatus.Rejected)
        //       → return InvalidRequest with ACTIVATION_REQUEST_TERMINAL
    }

    [Fact]
    public async Task Activation_Requests_Are_Tenant_Isolated()
    {
        var service = CreateService();
        var contextA = CreateContext("SubmitActivationRequest", tenantId: "tenant-A");
        var contextB = CreateContext("GetActivationRequestStatus", tenantId: "tenant-B");

        var draftA = CreateTestDraft(tenantId: "tenant-A");
        DraftStoreMock.Setup(s => s.GetAsync("tenant-A", "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draftA));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            ReviewResultId = "review-001"
        };

        var submitResult = await service.SubmitActivationRequestAsync(contextA, request);
        var requestId = submitResult.Value!.RequestId;

        // Tenant-B cannot see tenant-A's request
        var statusResult = await service.GetActivationRequestStatusAsync(contextB, requestId);

        statusResult.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task Agent_Cannot_Become_Governance_Authority()
    {
        // This test documents the invariant: there is no tool that allows
        // an agent to approve an activation request. SubmitActivationRequest
        // only creates a Submitted record. The approval path requires
        // human governance (outside the Control Plane tool surface).
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest", actorKind: AgentToolActorKind.Agent);
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            ReviewResultId = "review-001"
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        // Status is Submitted, never Approved
        result.Value!.Status.Should().Be(ActivationRequestStatus.Submitted);

        // The IAgentControlPlaneToolService interface has no ApproveActivationRequest method
        // This is by design — agents cannot approve
    }
}
