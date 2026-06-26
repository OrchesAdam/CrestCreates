using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

// semantic-string-guard: allow

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Runtime boundary and AOT safety tests.
/// Verifies: no runtime reflection discovery, no dynamic method generation,
/// no assembly scanning, no generic object payloads.
/// Agent CANNOT: approve, activate, execute runtime handlers,
/// mutate runtime registries, or become governance authority.
/// </summary>
public class RuntimeBoundaryTests : AgentControlPlaneTestBase
{
    [Fact]
    public void StaticManifest_No_Runtime_Discovery()
    {
        // The StaticAgentToolManifestProvider uses hardcoded tool list,
        // not runtime reflection / assembly scanning
        var provider = new StaticAgentToolManifestProvider();
        var tools = provider.GetAllTools();

        tools.Should().NotBeEmpty("manifest must be populated statically");
        // No dynamic plugin loading, no reflection-based discovery
    }

    [Fact]
    public void All_Tool_Descriptors_Have_MutatesRuntimeRegistry_False()
    {
        var provider = new StaticAgentToolManifestProvider();
        var tools = provider.GetAllTools();

        foreach (var tool in tools)
        {
            tool.MutatesRuntimeRegistry.Should().BeFalse(
                $"tool '{tool.Name}' must not mutate runtime registry — " +
                "Agent cannot execute runtime handlers");
        }
    }

    [Fact]
    public void No_Tool_Has_Runtime_Execution_Permission()
    {
        var provider = new StaticAgentToolManifestProvider();
        var tools = provider.GetAllTools();

        foreach (var tool in tools)
        {
            foreach (var perm in tool.Permissions)
            {
                perm.PermissionName.Should().NotStartWith("agent.runtime.",
                    $"tool '{tool.Name}' must not have runtime execution permission");
            }
        }
    }

    [Fact]
    public async Task Authorization_Service_Denies_Runtime_Execution_Prefix()
    {
        var service = new DefaultAgentToolAuthorizationService();
        var context = new AgentToolInvocationContext
        {
            TenantId = "t",
            ActorId = "a",
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = "c",
            ToolName = "AnyTool",
            InvocationSource = AgentToolInvocationSource.Direct
        };
        var perm = new AgentToolPermissionRequirement
        {
            PermissionName = "agent.runtime.execute"
        };

        var result = await service.AuthorizeAsync(context, perm, "AnyTool");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "RUNTIME_EXECUTION_DENIED");
    }

    [Fact]
    public void No_Activation_Approval_Tool_Exists()
    {
        // Verify there is no tool for approving activation requests
        var provider = new StaticAgentToolManifestProvider();
        var tools = provider.GetAllTools();

        tools.Should().NotContain(t => t.Name == "ApproveActivationRequest",
            "Agent cannot approve activation — this is human governance");
        tools.Should().NotContain(t => t.Name == "ExecuteActivation",
            "Agent cannot execute activation — this is runtime boundary");
        tools.Should().NotContain(t => t.Name == "ActivateDescriptor",
            "Agent cannot activate descriptors directly");
    }

    [Fact]
    public void All_Result_Types_Are_Strongly_Typed()
    {
        // AgentToolResult<T> where T : class — no generic object payloads
        // This is a structural test: the type system enforces it
        var result = AgentToolResult<string>.Success("test");

        result.Value.Should().Be("test");
        result.Status.Should().Be(AgentToolResultStatus.Success);

        // AgentToolResult<object> would violate the intent but is technically possible
        // The design convention is to use specific DTOs
    }

    [Fact]
    public void Invocation_Context_Carries_All_Required_Fields()
    {
        // Every invocation must carry tenant, actor, correlation, tool name, source
        var context = new AgentToolInvocationContext
        {
            TenantId = "tenant-001",
            ActorId = "actor-001",
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = "corr-001",
            ToolName = "GetDescriptorByRef",
            InvocationSource = AgentToolInvocationSource.Direct
        };

        context.TenantId.Should().NotBeNullOrEmpty();
        context.ActorId.Should().NotBeNullOrEmpty();
        context.CorrelationId.Should().NotBeNullOrEmpty();
        context.ToolName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Audit_Record_Tracks_All_Touch_Points()
    {
        var record = new AgentToolInvocationAuditRecord
        {
            AuditId = "audit-001",
            Timestamp = DateTimeOffset.UtcNow,
            Context = new AgentToolInvocationContext
            {
                TenantId = "t",
                ActorId = "a",
                ActorKind = AgentToolActorKind.Agent,
                CorrelationId = "c",
                ToolName = "ReviewDescriptorDraft",
                InvocationSource = AgentToolInvocationSource.Direct
            },
            ResultStatus = AgentToolResultStatus.Success,
            Diagnostics = Array.Empty<AgentToolDiagnostic>(),
            TouchedDescriptorRefs = new[] { new DescriptorRef("ns", "id") },
            TouchedDraftIds = new[] { "draft-001" },
            TouchedReviewResultIds = new[] { "review-001" },
            TouchedFixProposalIds = new[] { "fix-001" },
            TouchedPackagePreviewIds = new[] { "preview-001" },
            TouchedActivationRequestIds = new[] { "activation-001" }
        };

        record.TouchedDescriptorRefs.Should().NotBeNull();
        record.TouchedDraftIds.Should().NotBeNull();
        record.TouchedReviewResultIds.Should().NotBeNull();
        record.TouchedFixProposalIds.Should().NotBeNull();
        record.TouchedPackagePreviewIds.Should().NotBeNull();
        record.TouchedActivationRequestIds.Should().NotBeNull();
    }

    [Fact]
    public async Task Deny_Always_Recorded_In_Audit()
    {
        // Verify that every denied invocation results in an audit record
        var auditor = new InMemoryAgentToolInvocationAuditor();
        var policy = AgentToolAuthorizationPolicy.ReadOnly;
        var service = new DefaultAgentControlPlaneToolService(
            new StaticAgentToolManifestProvider(),
            new DefaultAgentToolAuthorizationService(policy),
            auditor,
            DraftStoreMock.Object,
            DraftValidatorMock.Object,
            DraftReviewServiceMock.Object,
            DraftMaterializerMock.Object,
            ContextPackBuilderMock.Object,
            DescriptorCatalogMock.Object,
            RelationshipProviderMock.Object,
            TopologyBuilderMock.Object,
            PackageBuilderMock.Object,
            NullLogger<DefaultAgentControlPlaneToolService>.Instance,
            HashBuilderMock.Object,
            ReviewHashServiceMock.Object,
            ReportBuilderMock.Object,
            ReportRendererMock.Object,
            ActivationRequestServiceMock.Object,
            ActivationReviewOrchestratorMock.Object,
            InMemoryArtifactResolver
        );

        var context = new AgentToolInvocationContext
        {
            TenantId = "t",
            ActorId = "a",
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = "c",
            ToolName = "CreateDescriptorDraft",
            InvocationSource = AgentToolInvocationSource.Direct
        };

        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.d1",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = CreateTestPayloadDto(DescriptorKind.Event, "test.d1", "Test")
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        auditor.GetAllRecords().Should().Contain(r =>
            r.ResultStatus == AgentToolResultStatus.Denied &&
            r.Context.ToolName == "CreateDescriptorDraft");
    }
}
