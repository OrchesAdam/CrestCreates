using Xunit;
using CrestCreates.Agent.ControlPlane.Abstractions;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests for the default authorization service and policy.
/// Verifies: runtime execution always denied, policy-based deny, agent intent ignored.
/// </summary>
public class AuthorizationTests
{
    [Fact]
    public async Task AllowAll_Policy_Allows_ControlPlane_Permission()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.AllowAll);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = Perm(AgentToolPermissionName.DescriptorRead);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Runtime_Execution_Always_Denied_Even_With_AllowAll_Policy()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.AllowAll);
        var context = CreateCtx("ExecuteRuntimeHandler");
        var perm = Perm("agent.runtime.execute");

        var result = await service.AuthorizeAsync(context, perm, "ExecuteRuntimeHandler");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "RUNTIME_EXECUTION_DENIED");
    }

    [Fact]
    public async Task Runtime_Execution_Prefix_Always_Denied()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.AllowAll);
        var context = CreateCtx("AnyTool");
        var perm = Perm("agent.runtime.whatever");

        var result = await service.AuthorizeAsync(context, perm, "AnyTool");

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ReadOnly_Policy_Denies_DraftCreate()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.ReadOnly);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = Perm(AgentToolPermissionName.DraftCreate);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "PERMISSION_DENIED");
    }

    [Fact]
    public async Task ReadOnly_Policy_Denies_ActivationRequestSubmit()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.ReadOnly);
        var context = CreateCtx("SubmitActivationRequest");
        var perm = Perm(AgentToolPermissionName.ActivationRequestSubmit);

        var result = await service.AuthorizeAsync(context, perm, "SubmitActivationRequest");

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ReadOnly_Policy_Allows_DescriptorRead()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.ReadOnly);
        var context = CreateCtx("GetDescriptorByRef");
        var perm = Perm(AgentToolPermissionName.DescriptorRead);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task DeniedToolName_Blocks_Access()
    {
        var policy = new AgentToolAuthorizationPolicy
        {
            DeniedToolNames = { "CreateDescriptorDraft" }
        };
        var service = new DefaultAgentToolAuthorizationService(policy);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = Perm(AgentToolPermissionName.DraftCreate);

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "TOOL_DENIED");
    }

    [Fact]
    public async Task DeniedToolName_Uses_ExpectedToolName_Not_ContextToolName()
    {
        // If context.ToolName is spoofed but expectedToolName is the real tool name,
        // the authorization service must use the authoritative expectedToolName.
        var policy = new AgentToolAuthorizationPolicy
        {
            DeniedToolNames = { "SubmitActivationRequest" }
        };
        var service = new DefaultAgentToolAuthorizationService(policy);

        // Caller tries to spoof: context says "BuildMetadataContextPack" but the real tool is "SubmitActivationRequest"
        var spoofedContext = CreateCtx("BuildMetadataContextPack");
        var perm = Perm(AgentToolPermissionName.ActivationRequestSubmit);

        var result = await service.AuthorizeAsync(spoofedContext, perm, "SubmitActivationRequest");

        // Must deny based on the authoritative expectedToolName, not the spoofed context
        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "TOOL_DENIED");
    }

    [Fact]
    public async Task DeniedActorKind_Blocks_Access()
    {
        var policy = new AgentToolAuthorizationPolicy
        {
            DeniedActorKinds = { AgentToolActorKind.Agent }
        };
        var service = new DefaultAgentToolAuthorizationService(policy);
        var context = CreateCtx("GetDescriptorByRef", actorKind: AgentToolActorKind.Agent);
        var perm = Perm(AgentToolPermissionName.DescriptorRead);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "ACTOR_KIND_DENIED");
    }

    [Fact]
    public async Task DeniedDescriptorKind_Blocks_Access()
    {
        var policy = new AgentToolAuthorizationPolicy
        {
            DeniedDescriptorKinds = { "Event" }
        };
        var service = new DefaultAgentToolAuthorizationService(policy);
        var context = CreateCtx("CreateDescriptorDraft");
        var perm = new AgentToolPermissionRequirement
        {
            PermissionName = AgentToolPermissionName.DraftCreate,
            DescriptorKindConstraint = "Event"
        };

        var result = await service.AuthorizeAsync(context, perm, "CreateDescriptorDraft");

        result.IsAllowed.Should().BeFalse();
        result.DenialDiagnostics.Should().ContainSingle(d => d.Code == "DESC_KIND_DENIED");
    }

    [Fact]
    public async Task Agent_Intent_Does_Not_Affect_Authorization()
    {
        // Even if agent declares "activation" intent in trace attributes, authorization must not change
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy.AllowAll);
        var context = CreateCtx("GetDescriptorByRef", traceAttributes: new Dictionary<string, string>
        {
            { "intent", "activate" },
            { "goal", "approve-draft" }
        });
        var perm = Perm(AgentToolPermissionName.DescriptorRead);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
        // The point is: intent did not cause denial or special behavior
    }

    [Fact]
    public async Task Default_Ctor_Uses_AllowAll_Policy()
    {
        var service = new DefaultAgentToolAuthorizationService();
        var context = CreateCtx("GetDescriptorByRef");
        var perm = Perm(AgentToolPermissionName.DescriptorRead);

        var result = await service.AuthorizeAsync(context, perm, "GetDescriptorByRef");

        result.IsAllowed.Should().BeTrue();
    }

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
}
