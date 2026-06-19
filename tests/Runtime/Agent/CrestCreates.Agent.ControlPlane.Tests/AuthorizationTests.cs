using Xunit;
using CrestCreates.Agent.ControlPlane.Abstractions;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests for the mode-driven authorization service.
/// Verifies: runtime execution always denied, mode-based defaults, explicit
/// allow/deny rules, deny-overrides-allow, category-aware decisions,
/// agent intent ignored, legacy policy compatibility.
/// </summary>
public class AuthorizationTests
{
    // ── DevelopmentAllowAll mode ──

    [Fact]
    public async Task DevelopmentAllowAll_Allows_ControlPlane_Permission()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task DevelopmentAllowAll_Allows_Mutating_Tool()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Runtime execution always denied ──

    [Fact]
    public async Task Runtime_Execution_Always_Denied_Even_With_DevelopmentAllowAll()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateCtx("ExecuteRuntimeHandler");
        var perm = Perm("agent.runtime.execute");

        var result = await service.AuthorizeAsync(context, perm, "ExecuteRuntimeHandler");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "RUNTIME_EXECUTION_DENIED");
    }

    [Fact]
    public async Task Runtime_Execution_Prefix_Always_Denied()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateCtx("AnyTool");
        var perm = Perm("agent.runtime.whatever");

        var result = await service.AuthorizeAsync(context, perm, "AnyTool");

        result.IsAllowed.Should().BeFalse();
    }

    // ── ExplicitPolicy mode — category defaults ──

    [Fact]
    public async Task ProductionDefaults_Denies_MutatingTools_WithoutExplicitPermission()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.ProductionDefaults);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "MUTATION_DENIED");
    }

    [Fact]
    public async Task ProductionDefaults_Denies_ActivationHandoffTools_WithoutExplicitPermission()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.ProductionDefaults);
        var context = CreateCtx("SubmitActivationRequest");
        var perm = MutatingPerm(AgentToolPermissionName.ActivationRequestSubmit, AgentToolCategory.ActivationHandoff);

        var result = await service.AuthorizeAsync(context, perm, "SubmitActivationRequest");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "ACTIVATION_HANDOFF_DENIED");
    }

    [Fact]
    public async Task ProductionDefaults_Allows_ReadOnlyTools()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.ProductionDefaults);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ProductionDefaults_Denies_ActivationHandoff_ReadTools()
    {
        // Even read-only activation handoff tools (GetActivationRequestStatus) are denied
        // because the ActivationHandoff category requires explicit permission.
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.ProductionDefaults);
        var context = CreateCtx("GetActivationRequestStatus");
        var perm = ReadPerm(AgentToolPermissionName.ActivationRequestRead, AgentToolCategory.ActivationHandoff);

        var result = await service.AuthorizeAsync(context, perm, "GetActivationRequestStatus");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "ACTIVATION_HANDOFF_DENIED");
    }

    // ── DevelopmentAllowAll policy (from issue requirement) ──

    [Fact]
    public async Task DevelopmentPolicy_Allows_AllTools_For_TestHarness()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateCtx("SubmitActivationRequest");
        var perm = MutatingPerm(AgentToolPermissionName.ActivationRequestSubmit, AgentToolCategory.ActivationHandoff);

        var result = await service.AuthorizeAsync(context, perm, "SubmitActivationRequest");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Explicit allow rules ──

    [Fact]
    public async Task ExplicitPolicy_Can_Allow_CreateDescriptorDraft()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = false,
            AllowedPermissions = { AgentToolPermissionName.DraftCreate }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ExplicitPolicy_Can_Allow_SubmitActivationRequest()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = false,
            AllowedPermissions = { AgentToolPermissionName.ActivationRequestSubmit }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("SubmitActivationRequest");
        var perm = MutatingPerm(AgentToolPermissionName.ActivationRequestSubmit, AgentToolCategory.ActivationHandoff);

        var result = await service.AuthorizeAsync(context, perm, "SubmitActivationRequest");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ExplicitPolicy_AllowedToolName_Grants_Access()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = false,
            AllowedToolNames = { "GetDescriptorByRef" }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Deny overrides allow ──

    [Fact]
    public async Task DeniedToolName_Overrides_AllowedPermission()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
            AllowedPermissions = { AgentToolPermissionName.DraftCreate },
            DeniedToolNames = { "CreateDescriptorDraft" }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "TOOL_DENIED");
    }

    [Fact]
    public async Task DeniedPermission_Overrides_AllowedToolName()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
            AllowedToolNames = { "CreateDescriptorDraft" },
            DeniedPermissions = { AgentToolPermissionName.DraftCreate }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "PERMISSION_DENIED");
    }

    // ── DenyAll mode ──

    [Fact]
    public async Task DenyAll_Denies_ReadOnlyTool_WithoutExplicitAllow()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.LockedDown);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "NOT_EXPLICITLY_ALLOWED");
    }

    [Fact]
    public async Task DenyAll_Allows_When_ExplicitPermissionGranted()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DenyAll,
            AllowedPermissions = { AgentToolPermissionName.DescriptorRead }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Tool name integrity ──

    [Fact]
    public async Task Authorization_Uses_ExpectedToolName_Not_ContextToolName()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
            DeniedToolNames = { "SubmitActivationRequest" }
        };
        var service = new DefaultAgentToolAuthorizationService(options);

        var spoofedContext = CreateCtx("BuildMetadataContextPack");
        var perm = MutatingPerm(AgentToolPermissionName.ActivationRequestSubmit, AgentToolCategory.ActivationHandoff);

        var result = await service.AuthorizeAsync(spoofedContext, perm, "SubmitActivationRequest");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "TOOL_DENIED");
    }

    // ── Denied descriptor kind / actor kind ──

    [Fact]
    public async Task DeniedActorKind_Blocks_Access()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
            DeniedActorKinds = { AgentToolActorKind.Agent }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("GetDescriptorByRef", actorKind: AgentToolActorKind.Agent);
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "ACTOR_KIND_DENIED");
    }

    [Fact]
    public async Task DeniedDescriptorKind_Blocks_Access()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
            DeniedDescriptorKinds = { "Event" }
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = new AgentToolPermissionRequirement
        {
            PermissionName = AgentToolPermissionName.DraftCreate,
            DescriptorKindConstraint = "Event",
            ToolCategory = AgentToolCategory.Draft,
            IsReadOnly = false
        };

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "DESC_KIND_DENIED");
    }

    // ── Agent intent ignored ──

    [Fact]
    public async Task Agent_Intent_Does_Not_Affect_Authorization()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.DevelopmentDefaults);
        var context = CreateCtx("GetDescriptorByRef", traceAttributes: new Dictionary<string, string>
        {
            { "intent", "activate" },
            { "goal", "approve-draft" }
        });
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Default constructor ──

    [Fact]
    public async Task Default_Ctor_Uses_ProductionDefaults()
    {
        var service = new DefaultAgentToolAuthorizationService();
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        // Production defaults deny mutating tools
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task Default_Ctor_Allows_ReadOnlyTools()
    {
        var service = new DefaultAgentToolAuthorizationService();
        var context = CreateCtx("GetDescriptorByRef");
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Legacy policy compatibility ──

    [Fact]
    public async Task LegacyPolicy_AllowAll_Converts_To_DevelopmentAllowAll()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.AllowAll);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task LegacyPolicy_ReadOnly_Denies_DraftCreate()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.ReadOnly);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "PERMISSION_DENIED");
    }

    [Fact]
    public async Task LegacyPolicy_ReadOnly_Allows_DescriptorRead()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.ReadOnly);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = ReadPerm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Category toggle: AllowMutationToolsByDefault ──

    [Fact]
    public async Task AllowMutationToolsByDefault_True_Allows_DraftCreate()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = true,
            AllowActivationHandoffToolsByDefault = false
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = MutatingPerm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task AllowActivationHandoffToolsByDefault_True_Allows_SubmitActivation()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = false,
            AllowActivationHandoffToolsByDefault = true
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("SubmitActivationRequest");
        var perm = MutatingPerm(AgentToolPermissionName.ActivationRequestSubmit, AgentToolCategory.ActivationHandoff);

        var result = await service.AuthorizeAsync(context, perm, "SubmitActivationRequest");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task AllowActivationHandoffToolsByDefault_True_Also_Allows_ReadTools_In_Category()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = false,
            AllowActivationHandoffToolsByDefault = true
        };
        var service = new DefaultAgentToolAuthorizationService(options);
        var context = CreateCtx("GetActivationRequestStatus");
        var perm = ReadPerm(AgentToolPermissionName.ActivationRequestRead, AgentToolCategory.ActivationHandoff);

        var result = await service.AuthorizeAsync(context, perm, "GetActivationRequestStatus");

        result.IsAllowed.Should().BeTrue();
    }

    // ── Helpers ──

    private static AgentToolInvocationContext CreateCtx(
        string toolName,
        AgentToolActorKind actorKind = AgentToolActorKind.Agent,
        IReadOnlyDictionary<string, string>? traceAttributes = null)
    {
        return new AgentToolInvocationContext
        {
            TenantId = "tenant-001",
            ActorId = "actor-001",
            ActorKind = actorKind,
            CorrelationId = "corr-001",
            ToolName = toolName,
            InvocationSource = AgentToolInvocationSource.Direct,
            TraceAttributes = traceAttributes
        };
    }

    private static AgentToolPermissionRequirement Perm(string name, string? desc = null)
        => new() { PermissionName = name, Description = desc };

    private static AgentToolPermissionRequirement ReadPerm(string name, AgentToolCategory category)
        => new() { PermissionName = name, ToolCategory = category, IsReadOnly = true };

    private static AgentToolPermissionRequirement MutatingPerm(string name, AgentToolCategory category)
        => new() { PermissionName = name, ToolCategory = category, IsReadOnly = false };
}
