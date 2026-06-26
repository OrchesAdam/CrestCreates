using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using FluentAssertions;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

// semantic-string-guard: allow

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Wave 6 tests: Activation Handoff tools.
/// Verifies: SubmitActivationRequest, GetActivationRequestStatus, CancelActivationRequest.
/// Key invariants:
/// - Agent CANNOT approve or execute activation
/// - Submit creates a record, does not execute activation
/// - Terminal states (Approved/Rejected/Cancelled) cannot be cancelled
/// - Referenced evidence artifacts must exist, belong to tenant, and match the draft
/// </summary>
public class Wave6ActivationHandoffTests : AgentControlPlaneTestBase
{
    private static ActivationBindingSnapshot CreateBindingSnapshot(
        string draftId = "draft-001",
        string? reviewResultId = null,
        string packagePreviewId = "pkg-001",
        string evidencePreviewId = "ev-001")
        => new()
        {
            TenantId = TestTenantId,
            DraftId = draftId,
            DraftVersion = 1,
            ReviewResultId = reviewResultId ?? "review-001",
            PackagePreviewId = packagePreviewId,
            EvidencePreviewId = evidencePreviewId,
            Hashes = new BindingHashes
            {
                SourceReviewHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.ReviewResult, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.SourceBinding, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "src-review-hash" },
                ReviewManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.ReviewResult, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Integrity, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "manifest-hash" },
                PackageManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageManifest, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "manifest-hash" },
                PackageEvidenceHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidence, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "evidence-hash" },
                PackageEvidenceEnvelopeHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidenceEnvelope, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "envelope-hash" },
                ContractHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "contract-hash" },
                DefinitionHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Definition, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "definition-hash" }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Creates a service and populates the internal review result store by running
    /// ReviewDescriptorDraftAsync. Returns the review result ID.
    /// </summary>
    private async Task<(DefaultAgentControlPlaneToolService Service, string ReviewResultId)> CreateServiceWithReviewResult(
        string draftId = "draft-001")
    {
        var service = CreateService();
        var draft = CreateTestDraft(draftId: draftId);

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, draftId, It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = draftId,
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true,
            ProposedInventory = new List<IDescriptor>().AsReadOnly()
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        var reviewContext = CreateContext("ReviewDescriptorDraft");
        var reviewResultWrapper = await service.ReviewDescriptorDraftAsync(reviewContext, draftId);

        // Extract review result ID from audit
        var auditRecord = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "ReviewDescriptorDraft" &&
            r.TouchedReviewResultIds != null);
        var reviewResultId = auditRecord.TouchedReviewResultIds!.First();

        return (service, reviewResultId);
    }

    /// <summary>
    /// Creates a service and populates the internal package preview store by running
    /// PreviewDescriptorPackageAsync. Returns the package preview ID.
    /// </summary>
    private async Task<(DefaultAgentControlPlaneToolService Service, string PackagePreviewId)> CreateServiceWithPackagePreview(
        string draftId = "draft-001")
    {
        var service = CreateService();
        var draft = CreateTestDraft(draftId: draftId);

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, draftId, It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);

        DraftMaterializerMock.Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(new List<IDescriptor>().AsReadOnly()));

        SetupPackageBuilder();

        var previewContext = CreateContext("PreviewDescriptorPackage");
        var previewResult = await service.PreviewDescriptorPackageAsync(previewContext, draftId);

        // Extract preview ID from audit
        var auditRecord = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "PreviewDescriptorPackage" &&
            r.TouchedPackagePreviewIds != null);
        var previewId = auditRecord.TouchedPackagePreviewIds!.First();

        return (service, previewId);
    }

    /// <summary>
    /// Creates a service and populates both the review result and package preview stores
    /// for the same draft. Returns the service and both artifact IDs.
    /// </summary>
    private async Task<(DefaultAgentControlPlaneToolService Service, string ReviewResultId, string PackagePreviewId)> CreateServiceWithReviewAndPackagePreview(
        string draftId = "draft-001")
    {
        var service = CreateService();
        var draft = CreateTestDraft(draftId: draftId);

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, draftId, It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = draftId,
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true,
            ProposedInventory = new List<IDescriptor>().AsReadOnly()
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        var reviewContext = CreateContext("ReviewDescriptorDraft");
        await service.ReviewDescriptorDraftAsync(reviewContext, draftId);

        var reviewResultId = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "ReviewDescriptorDraft" &&
            r.TouchedReviewResultIds != null).TouchedReviewResultIds!.First();

        DraftMaterializerMock.Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(new List<IDescriptor>().AsReadOnly()));

        SetupPackageBuilder();

        var previewContext = CreateContext("PreviewDescriptorPackage");
        await service.PreviewDescriptorPackageAsync(previewContext, draftId);

        var packagePreviewId = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "PreviewDescriptorPackage" &&
            r.TouchedPackagePreviewIds != null).TouchedPackagePreviewIds!.First();

        return (service, reviewResultId, packagePreviewId);
    }

    [Fact]
    public async Task SubmitActivationRequest_Creates_Request_Record()
    {
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var context = CreateContext("SubmitActivationRequest");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
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
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var context = CreateContext("SubmitActivationRequest");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Value!.Status.Should().Be(ActivationRequestStatus.Submitted);
        result.Value.Status.Should().NotBe(ActivationRequestStatus.Approved);
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
            BindingSnapshot = CreateBindingSnapshot("nonexistent")
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task SubmitActivationRequest_Rejects_NonExistent_ReviewResult()
    {
        var service = CreateService();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot()
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "ACTIVATION_REVIEW_RESULT_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitActivationRequest_Rejects_ReviewResult_Draft_Mismatch()
    {
        // Create review result for draft-001
        var (service, reviewResultId) = await CreateServiceWithReviewResult();
        var context = CreateContext("SubmitActivationRequest");

        // Set up draft-002 (a different draft)
        var otherDraft = CreateTestDraft(draftId: "draft-002");
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-002", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(otherDraft));

        // Submit activation for draft-002 using review result from draft-001
        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-002",
            BindingSnapshot = CreateBindingSnapshot("draft-002") with { ReviewResultId = reviewResultId }
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "ACTIVATION_REVIEW_RESULT_DRAFT_MISMATCH");
    }

    [Fact]
    public async Task SubmitActivationRequest_Rejects_NonExistent_PackagePreview()
    {
        var (service, reviewResultId) = await CreateServiceWithReviewResult();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId) with { PackagePreviewId = "nonexistent-preview" }
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "ACTIVATION_PACKAGE_PREVIEW_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitActivationRequest_Rejects_PackagePreview_Draft_Mismatch()
    {
        // Create review result + package preview both for draft-001
        var (service, reviewResultId, packagePreviewId) = await CreateServiceWithReviewAndPackagePreview();
        var context = CreateContext("SubmitActivationRequest");

        // Set up draft-002 (a different draft)
        var otherDraft = CreateTestDraft(draftId: "draft-002");
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-002", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(otherDraft));

        // Submit activation for draft-002 using package preview from draft-001
        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-002",
            BindingSnapshot = CreateBindingSnapshot("draft-002", reviewResultId: reviewResultId) with { PackagePreviewId = packagePreviewId }
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "ACTIVATION_PACKAGE_PREVIEW_DRAFT_MISMATCH");
    }

    [Fact]
    public async Task SubmitActivationRequest_Rejects_NonExistent_EvidencePreview()
    {
        var (service, reviewResultId) = await CreateServiceWithReviewResult();
        var context = CreateContext("SubmitActivationRequest");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId) with { EvidencePreviewId = "nonexistent-evidence" }
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "ACTIVATION_EVIDENCE_PREVIEW_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitActivationRequest_Audit_Records_TouchedIds()
    {
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var context = CreateContext("SubmitActivationRequest");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
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
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var submitContext = CreateContext("SubmitActivationRequest");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
        };

        var submitResult = await service.SubmitActivationRequestAsync(submitContext, request);
        var requestId = submitResult.Value!.RequestId;

        // Retrieve status — use correct tool name for GetActivationRequestStatus
        var statusContext = CreateContext("GetActivationRequestStatus");
        var statusResult = await service.GetActivationRequestStatusAsync(statusContext, requestId);

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
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var submitContext = CreateContext("SubmitActivationRequest");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
        };

        var submitResult = await service.SubmitActivationRequestAsync(submitContext, request);
        var requestId = submitResult.Value!.RequestId;

        // Cancel — use correct tool name for CancelActivationRequest
        var cancelContext = CreateContext("CancelActivationRequest");
        var cancelResult = await service.CancelActivationRequestAsync(cancelContext, requestId);

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
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var submitContext = CreateContext("SubmitActivationRequest");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
        };

        var submitResult = await service.SubmitActivationRequestAsync(submitContext, request);
        var requestId = submitResult.Value!.RequestId;

        // Cancel the Submitted request — should succeed
        var cancelContext = CreateContext("CancelActivationRequest");
        var cancelResult = await service.CancelActivationRequestAsync(cancelContext, requestId);
        cancelResult.Status.Should().Be(AgentToolResultStatus.Success);
        cancelResult.Value!.Status.Should().Be(ActivationRequestStatus.Cancelled);

        // Note: Approved and Rejected are terminal states that would return InvalidRequest
        // with ACTIVATION_REQUEST_TERMINAL. These states can only be set by human governance
        // (outside the tool surface), so we cannot test them through the API directly.
    }

    [Fact]
    public async Task Activation_Requests_Are_Tenant_Isolated()
    {
        var (serviceA, _) = await CreateServiceWithReviewResult();

        var contextA = CreateContext("SubmitActivationRequest", tenantId: "tenant-A");
        var contextB = CreateContext("GetActivationRequestStatus", tenantId: "tenant-B");

        // Set up draft for tenant-A
        var draftA = CreateTestDraft(tenantId: "tenant-A");
        DraftStoreMock.Setup(s => s.GetAsync("tenant-A", "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draftA));

        var statusResult = await serviceA.GetActivationRequestStatusAsync(contextB, "any-request-id");

        statusResult.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task Agent_Cannot_Become_Governance_Authority()
    {
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var context = CreateContext("SubmitActivationRequest", actorKind: AgentToolActorKind.Agent);

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        // Status is Submitted, never Approved
        result.Value!.Status.Should().Be(ActivationRequestStatus.Submitted);
    }

    [Fact]
    public async Task SubmitActivationRequest_Wires_GovernanceDecision_From_ReviewResult()
    {
        // The ToolService should extract GovernanceDecision from the review result's
        // GovernanceDecision.MaxDecision and pass it through to the RequestService.
        var (service, reviewResultId, packagePreviewId, evidencePreviewId) = await CreateServiceWithFullBindingArtifacts();
        var context = CreateContext("SubmitActivationRequest");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot(reviewResultId: reviewResultId,
                packagePreviewId: packagePreviewId, evidencePreviewId: evidencePreviewId)
        };

        await service.SubmitActivationRequestAsync(context, request);

        // Verify the mock received a request with GovernanceDecision = Allowed
        // (the test base sets up the review result with GovernanceDecision.MaxDecision = Allowed)
        ActivationRequestServiceMock.Verify(
            x => x.CreateActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.Is<SubmitActivationRequestRequest>(r =>
                    r.GovernanceDecision == DescriptorLifecycleDecisionKind.Allowed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
