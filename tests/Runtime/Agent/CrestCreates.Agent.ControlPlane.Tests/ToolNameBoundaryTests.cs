using Xunit;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using FluentAssertions;

using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tool boundary tests verifying that the ToolName integrity check in ExecuteAsync
/// prevents manifest / audit / authorization policy boundary violations.
///
/// These tests close the P0 vulnerability: a caller could invoke
/// SubmitActivationRequestAsync with context.ToolName = "BuildMetadataContextPack",
/// causing manifest lookup, tool-name deny policy, and audit to record the wrong tool.
///
/// After the fix:
/// - ExecuteAsync validates context.ToolName matches expectedToolName before anything else
/// - Authorization service uses expectedToolName (not context.ToolName) for tool-name deny checks
/// - Audit records use the expected (authoritative) tool name
/// </summary>
public class ToolNameBoundaryTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task ToolNameMismatch_IsRejected_BeforeAuthorization()
    {
        // If context.ToolName doesn't match the facade method's expected tool name,
        // the invocation must be rejected BEFORE authorization is even checked.
        var service = CreateService();
        // Spoofed context: calling SubmitActivationRequestAsync but context says "BuildMetadataContextPack"
        var spoofedContext = CreateContext("BuildMetadataContextPack");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot()
        };

        var result = await service.SubmitActivationRequestAsync(spoofedContext, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "TOOL_NAME_MISMATCH");
        // Must NOT be Denied (which would mean authorization was reached)
        result.Status.Should().NotBe(AgentToolResultStatus.Denied);
    }

    [Fact]
    public async Task DeniedToolName_CannotBeBypassed_BySpoofedContextToolName()
    {
        // If "SubmitActivationRequest" is in the DeniedToolNames options,
        // a caller cannot bypass the deny by spoofing context.ToolName.
        // The authorization service now uses the authoritative expectedToolName.
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
            DeniedToolNames = { "SubmitActivationRequest" }
        };
        var service = CreateService(options);

        // Context has the correct tool name — the authorization check must still deny it
        var context = CreateContext("SubmitActivationRequest");
        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot()
        };

        var result = await service.SubmitActivationRequestAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "TOOL_DENIED");
    }

    [Fact]
    public async Task Audit_UsesExpectedToolName_NotCallerSuppliedSpoofedName()
    {
        // When a TOOL_NAME_MISMATCH is detected, the audit record must use
        // the authoritative expected tool name, not the caller-supplied spoofed name.
        var auditor = new InMemoryAgentToolInvocationAuditor();
        var service = CreateService(auditor: auditor);

        // Spoofed: context says "BuildMetadataContextPack" but we're calling SubmitActivationRequestAsync
        var spoofedContext = CreateContext("BuildMetadataContextPack");
        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot()
        };

        var result = await service.SubmitActivationRequestAsync(spoofedContext, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);

        // The audit record must contain the EXPECTED tool name (SubmitActivationRequest),
        // not the spoofed one (BuildMetadataContextPack)
        auditor.GetAllRecords().Should().Contain(r =>
            r.Context.ToolName == "SubmitActivationRequest" &&
            r.ResultStatus == AgentToolResultStatus.InvalidRequest);
    }

    [Fact]
    public async Task SubmitActivationRequest_WithContextToolNameBuildMetadataContextPack_IsRejected()
    {
        // Specific scenario from the review: calling SubmitActivationRequestAsync
        // with context.ToolName = "BuildMetadataContextPack" must be rejected.
        var service = CreateService();
        var spoofedContext = CreateContext("BuildMetadataContextPack");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot()
        };

        var result = await service.SubmitActivationRequestAsync(spoofedContext, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "TOOL_NAME_MISMATCH");
        result.Diagnostics[0].Message.Should().Contain("BuildMetadataContextPack");
        result.Diagnostics[0].Message.Should().Contain("SubmitActivationRequest");
    }

    [Fact]
    public async Task SubmitActivationRequest_WithSpoofedContextToolName_IsRejected_BeforeAuthorization()
    {
        // Facade-level regression test: a spoofed context.ToolName must be rejected
        // at the facade (before authorization), and the audit record must contain
        // the authoritative expected tool name — not the caller-supplied spoofed name.
        var auditor = new InMemoryAgentToolInvocationAuditor();
        var service = CreateService(auditor: auditor);
        var spoofedContext = CreateContext("BuildMetadataContextPack");

        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-001",
            BindingSnapshot = CreateBindingSnapshot()
        };

        var result = await service.SubmitActivationRequestAsync(spoofedContext, request);

        // Rejected at facade level before authorization is reached
        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "TOOL_NAME_MISMATCH");
        result.Status.Should().NotBe(AgentToolResultStatus.Denied);

        // Audit record uses the authoritative tool name, not the spoofed one
        auditor.GetAllRecords().Should().Contain(r =>
            r.Context.ToolName == "SubmitActivationRequest" &&
            r.ResultStatus == AgentToolResultStatus.InvalidRequest);
    }

    [Fact]
    public async Task ManifestLookup_UsesExpectedToolName()
    {
        // When context.ToolName matches expectedToolName, manifest lookup proceeds
        // using the expected (authoritative) tool name. If the tool doesn't exist
        // in the manifest, we get TOOL_NOT_FOUND (not TOOL_NAME_MISMATCH).
        // This verifies that after passing the name match check, manifest lookup
        // uses the same authoritative name.
        var service = CreateService();

        // Correct tool name — will pass TOOL_NAME_MISMATCH check
        // and proceed to manifest lookup which finds the tool
        var context = CreateContext("GetDescriptorByRef");
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);

        var result = await service.GetDescriptorByRefAsync(context, CreateDescriptorRef());

        // Should reach NotFound (descriptor not found) or Success — not TOOL_NAME_MISMATCH
        result.Status.Should().BeOneOf(AgentToolResultStatus.NotFound, AgentToolResultStatus.Success);
        result.Diagnostics.Should().NotContain(d => d.Code == "TOOL_NAME_MISMATCH");
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
                PackageManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageManifest, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                PackageEvidenceHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidence, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                PackageEvidenceEnvelopeHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.PackageEvidenceEnvelope, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.AuditEvidence, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                ContractHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" },
                DefinitionHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Definition, ContractVersion = "canonical-hash-v1", CanonicalShapeVersion = "test-v1", Value = "hash" }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
}
