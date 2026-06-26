using Xunit;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using FluentAssertions;
using Moq;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests for the permission boundary and audit recording invariants.
/// Every tool invocation must: manifest lookup → permission check → service invocation → audit recording.
/// These tests verify the boundary is enforced for denied, failed, and unknown tool invocations.
/// </summary>
public class PermissionBoundaryTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task ToolName_Mismatch_Is_Rejected_Before_Manifest_Lookup()
    {
        // If context.ToolName does not match the expected tool name for the facade method,
        // the invocation is rejected with TOOL_NAME_MISMATCH before any manifest lookup,
        // authorization, or action execution occurs.
        var service = CreateServiceWithMocks();
        var context = CreateContext("NonExistentTool");

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "TOOL_NAME_MISMATCH");
    }

    [Fact]
    public async Task Denied_Permission_Returns_Denied_Status()
    {
        var service = CreateServiceWithMocks();
        var context = CreateContext("GetDescriptorByRef");

        // Must set up manifest so the tool is found before authorization is checked
        ManifestProviderMock.Setup(p => p.GetToolByName("GetDescriptorByRef"))
            .Returns(new AgentToolDescriptor
            {
                Name = "GetDescriptorByRef",
                Description = "Test",
                Category = AgentToolCategory.Context,
                Permissions = [new AgentToolPermissionRequirement { PermissionName = AgentToolPermissionNames.DescriptorRead }],
                AllowedActors = [AgentToolActorKind.Agent],
                IsReadOnly = true,
                MutatesRuntimeRegistry = false
            });

        AuthorizationServiceMock
            .Setup(a => a.AuthorizeAsync(It.IsAny<AgentToolInvocationContext>(), It.IsAny<AgentToolPermissionRequirement>(), It.IsAny<string>()))
            .Returns(Task.FromResult(AgentToolAuthorizationResult.Denied(new AgentToolDiagnostic
            {
                Code = "PERMISSION_DENIED",
                Severity = AgentToolDiagnosticSeverity.Error,
                Message = "Not allowed"
            })));

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Denied_Permission_Records_Audit_With_Denied_Status()
    {
        var service = CreateServiceWithMocks();
        var context = CreateContext("GetDescriptorByRef");

        // Must set up manifest so the tool is found before authorization is checked
        ManifestProviderMock.Setup(p => p.GetToolByName("GetDescriptorByRef"))
            .Returns(new AgentToolDescriptor
            {
                Name = "GetDescriptorByRef",
                Description = "Test",
                Category = AgentToolCategory.Context,
                Permissions = [new AgentToolPermissionRequirement { PermissionName = AgentToolPermissionNames.DescriptorRead }],
                AllowedActors = [AgentToolActorKind.Agent],
                IsReadOnly = true,
                MutatesRuntimeRegistry = false
            });

        AuthorizationServiceMock
            .Setup(a => a.AuthorizeAsync(It.IsAny<AgentToolInvocationContext>(), It.IsAny<AgentToolPermissionRequirement>(), It.IsAny<string>()))
            .Returns(Task.FromResult(AgentToolAuthorizationResult.Denied(new AgentToolDiagnostic
            {
                Code = "PERMISSION_DENIED",
                Severity = AgentToolDiagnosticSeverity.Error,
                Message = "Not allowed"
            })));

        await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        AuditorMock.Verify(a => a.RecordAsync(
            It.Is<AgentToolInvocationAuditRecord>(r => r.ResultStatus == AgentToolResultStatus.Denied),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProductionDefaults_Denies_DraftCreate()
    {
        var service = CreateService(AgentToolAuthorizationOptions.ProductionDefaults);
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = CreateTestPayloadDto(DescriptorKind.Event, "test.desc-001", "Test")
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
    }

    [Fact]
    public async Task ProductionDefaults_Denies_ActivationRequestSubmit()
    {
        var service = CreateService(AgentToolAuthorizationOptions.ProductionDefaults);
        var context = CreateContext("SubmitActivationRequest");
        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot()
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
    }

    [Fact]
    public async Task Successful_Invocation_Records_Audit_With_Success_Status()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef();
        var descriptor = CreateTestDescriptor();

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([descriptor]);

        await service.GetDescriptorByRefAsync(context, descRef);

        InMemoryAuditor.GetAllRecords().Should().Contain(r =>
            r.Context.ToolName == "GetDescriptorByRef" &&
            r.ResultStatus == AgentToolResultStatus.Success);
    }

    [Fact]
    public async Task NotFound_Invocation_Records_Audit()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef();

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);

        var result = await service.GetDescriptorByRefAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
        InMemoryAuditor.GetAllRecords().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Every_Invocation_Records_Audit_Regardless_Of_Outcome()
    {
        var auditor = new InMemoryAgentToolInvocationAuditor();

        // Denied invocation
        var deniedSvc = CreateService(AgentToolAuthorizationOptions.ProductionDefaults, auditor);
        var deniedCtx = CreateContext("CreateDescriptorDraft");
        var deniedReq = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = CreateTestPayloadDto(DescriptorKind.Event, "test.desc-001", "Test")
        };
        await deniedSvc.CreateDescriptorDraftAsync(deniedCtx, deniedReq);

        // Successful invocation
        var svc = CreateService(auditor: auditor);
        var successCtx = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef();
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([CreateTestDescriptor()]);
        await svc.GetDescriptorByRefAsync(successCtx, descRef);

        auditor.GetAllRecords().Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Audit_Record_Contains_TenantId_From_Context()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef", tenantId: "tenant-ABC");
        var descRef = CreateDescriptorRef();

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([CreateTestDescriptor()]);

        await service.GetDescriptorByRefAsync(context, descRef);

        InMemoryAuditor.GetAllRecords().Should().Contain(r =>
            r.Context.TenantId == "tenant-ABC");
    }

    [Fact]
    public async Task Audit_Record_Contains_ActorId_From_Context()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef", actorId: "actor-XYZ");
        var descRef = CreateDescriptorRef();

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([CreateTestDescriptor()]);

        await service.GetDescriptorByRefAsync(context, descRef);

        InMemoryAuditor.GetAllRecords().Should().Contain(r =>
            r.Context.ActorId == "actor-XYZ");
    }

    [Fact]
    public async Task Service_Exception_Results_In_Failed_Status_With_Audit()
    {
        var service = CreateServiceWithMocks();
        var context = CreateContext("GetDescriptorByRef");

        // Set up manifest to return a tool so we pass manifest lookup
        ManifestProviderMock.Setup(p => p.GetToolByName("GetDescriptorByRef"))
            .Returns(new AgentToolDescriptor
            {
                Name = "GetDescriptorByRef",
                Description = "Test",
                Category = AgentToolCategory.Context,
                Permissions = [new AgentToolPermissionRequirement { PermissionName = AgentToolPermissionNames.DescriptorRead }],
                AllowedActors = [AgentToolActorKind.Agent],
                IsReadOnly = true,
                MutatesRuntimeRegistry = false
            });

        AuthorizationServiceMock
            .Setup(a => a.AuthorizeAsync(It.IsAny<AgentToolInvocationContext>(), It.IsAny<AgentToolPermissionRequirement>(), It.IsAny<string>()))
            .Returns(Task.FromResult(AgentToolAuthorizationResult.Allowed()));

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Throws(new InvalidOperationException("Catalog failure"));

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "TOOL_INVOCATION_FAILED");

        AuditorMock.Verify(a => a.RecordAsync(
            It.Is<AgentToolInvocationAuditRecord>(r => r.ResultStatus == AgentToolResultStatus.Failed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ActivationBindingSnapshot CreateBindingSnapshot()
        => new()
        {
            TenantId = TestTenantId,
            DraftId = "draft-001",
            DraftVersion = 1,
            ReviewResultId = "review-001",
            PackagePreviewId = "pkg-001",
            EvidencePreviewId = "ev-001",
            Hashes = new BindingHashes
            {
                SourceReviewHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.ReviewResult, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.SourceBinding, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                ReviewManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.ReviewResult, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Integrity, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                PackageManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageManifest, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Integrity, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                PackageEvidenceHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidence, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                PackageEvidenceEnvelopeHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidenceEnvelope, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                ContractHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                DefinitionHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Definition, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
}
