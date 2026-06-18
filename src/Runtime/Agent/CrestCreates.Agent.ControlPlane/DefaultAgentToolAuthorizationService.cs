using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Configurable allow/deny authorization service.
/// Agent intent (TraceAttributes) does NOT affect authorization decisions.
/// Runtime execution tools are denied by default.
/// </summary>
public sealed class DefaultAgentToolAuthorizationService : IAgentToolAuthorizationService
{
    private readonly AgentToolAuthorizationPolicy _policy;

    public DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy? policy = null)
    {
        _policy = policy ?? AgentToolAuthorizationPolicy.AllowAll;
    }

    public Task<AgentToolAuthorizationResult> AuthorizeAsync(
        AgentToolInvocationContext context,
        AgentToolPermissionRequirement permission,
        CancellationToken ct = default)
    {
        // 1. Runtime execution is always denied
        if (permission.PermissionName.StartsWith("agent.runtime.", StringComparison.Ordinal))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "RUNTIME_EXECUTION_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Blocker,
                    Message = "Runtime execution tools are not available through the Control Plane tool surface."
                }));
        }

        // 2. Check deny list by permission name
        if (_policy.DeniedPermissionNames.Contains(permission.PermissionName))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "PERMISSION_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Permission '{permission.PermissionName}' is denied by policy."
                }));
        }

        // 3. Check deny list by descriptor kind constraint
        if (permission.DescriptorKindConstraint is not null &&
            _policy.DeniedDescriptorKinds.Contains(permission.DescriptorKindConstraint))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "DESC_KIND_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Descriptor kind '{permission.DescriptorKindConstraint}' is denied by policy."
                }));
        }

        // 4. Check deny list by tool name (from context)
        if (_policy.DeniedToolNames.Contains(context.ToolName))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "TOOL_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Tool '{context.ToolName}' is denied by policy."
                }));
        }

        // 5. Check actor kind
        if (_policy.DeniedActorKinds.Contains(context.ActorKind))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "ACTOR_KIND_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Actor kind '{context.ActorKind}' is denied by policy."
                }));
        }

        // 6. Agent intent does not affect authorization (explicitly ignored)
        // TraceAttributes are not checked for authorization decisions.

        return Task.FromResult(AgentToolAuthorizationResult.Allowed());
    }
}
