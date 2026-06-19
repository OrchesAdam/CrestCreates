using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Mode-driven authorization service for the Agent Control Plane tool surface.
///
/// <para>Authorization decision flow:</para>
/// <list type="number">
///   <item>Runtime execution (<c>agent.runtime.*</c>) is always denied</item>
///   <item>Explicit deny rules (DeniedPermissions, DeniedToolNames, DeniedDescriptorKinds, DeniedActorKinds)
///         always win — deny overrides allow</item>
///   <item>Mode-based default stance is applied</item>
///   <item>Explicit allow rules (AllowedPermissions, AllowedToolNames) grant access
///         regardless of category defaults</item>
///   <item>Category defaults (AllowReadOnlyToolsByDefault, AllowMutationToolsByDefault,
///         AllowActivationHandoffToolsByDefault) are evaluated in ExplicitPolicy mode</item>
/// </list>
///
/// <para>Agent intent (TraceAttributes) does NOT affect authorization decisions.</para>
/// </summary>
public sealed class DefaultAgentToolAuthorizationService : IAgentToolAuthorizationService
{
    private readonly AgentToolAuthorizationOptions _options;

    /// <summary>
    /// Creates a new authorization service with the specified options.
    /// Defaults to <see cref="AgentToolAuthorizationOptions.ProductionDefaults"/> if null.
    /// </summary>
    public DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions? options = null)
    {
        _options = options ?? AgentToolAuthorizationOptions.ProductionDefaults;
    }

    /// <summary>
    /// Legacy constructor accepting a policy. Converts to equivalent options.
    /// </summary>
    public DefaultAgentToolAuthorizationService(AgentToolAuthorizationPolicy policy)
    {
        _options = PolicyToOptions(policy);
    }

    public Task<AgentToolAuthorizationResult> AuthorizeAsync(
        AgentToolInvocationContext context,
        AgentToolPermissionRequirement permission,
        string expectedToolName,
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

        // 2. Explicit deny rules always win — deny overrides allow

        if (_options.DeniedPermissions.Contains(permission.PermissionName))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "PERMISSION_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Permission '{permission.PermissionName}' is denied by policy."
                }));
        }

        if (_options.DeniedDescriptorKinds.Count > 0 &&
            permission.DescriptorKindConstraint is not null &&
            _options.DeniedDescriptorKinds.Contains(permission.DescriptorKindConstraint))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "DESC_KIND_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Descriptor kind '{permission.DescriptorKindConstraint}' is denied by policy."
                }));
        }

        // Uses the authoritative expectedToolName, not the caller-supplied context.ToolName
        if (_options.DeniedToolNames.Contains(expectedToolName))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "TOOL_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Tool '{expectedToolName}' is denied by policy."
                }));
        }

        if (_options.DeniedActorKinds.Contains(context.ActorKind))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Denied(
                new AgentToolDiagnostic
                {
                    Code = "ACTOR_KIND_DENIED",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Actor kind '{context.ActorKind}' is denied by policy."
                }));
        }

        // 3. Mode-based default stance

        switch (_options.Mode)
        {
            case AgentToolAuthorizationMode.DevelopmentAllowAll:
                // Everything allowed (except runtime execution and explicit denies above)
                return Task.FromResult(AgentToolAuthorizationResult.Allowed());

            case AgentToolAuthorizationMode.DenyAll:
                // Must be explicitly allowed
                return EvaluateExplicitAllow(permission, expectedToolName);

            case AgentToolAuthorizationMode.ExplicitPolicy:
                // Check explicit allow first, then category defaults
                return EvaluateExplicitPolicy(permission, expectedToolName);

            default:
                // Unknown mode → fail-closed: deny
                return Task.FromResult(AgentToolAuthorizationResult.Denied(
                    new AgentToolDiagnostic
                    {
                        Code = "UNKNOWN_AUTHORIZATION_MODE",
                        Severity = AgentToolDiagnosticSeverity.Blocker,
                        Message = $"Authorization mode '{_options.Mode}' is not recognized."
                    }));
        }
    }

    /// <summary>
    /// Evaluate whether the permission/tool is explicitly allowed.
    /// Used by DenyAll mode (only explicit allow grants access).
    /// </summary>
    private Task<AgentToolAuthorizationResult> EvaluateExplicitAllow(
        AgentToolPermissionRequirement permission,
        string expectedToolName)
    {
        if (_options.AllowedPermissions.Contains(permission.PermissionName) ||
            _options.AllowedToolNames.Contains(expectedToolName))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Allowed());
        }

        return Task.FromResult(AgentToolAuthorizationResult.Denied(
            new AgentToolDiagnostic
            {
                Code = "NOT_EXPLICITLY_ALLOWED",
                Severity = AgentToolDiagnosticSeverity.Error,
                Message = $"Permission '{permission.PermissionName}' for tool '{expectedToolName}' is not explicitly allowed in DenyAll mode."
            }));
    }

    /// <summary>
    /// Evaluate authorization in ExplicitPolicy mode.
    /// Explicit allow rules grant access regardless of category defaults.
    /// Category defaults control tools that are not explicitly allowed or denied.
    /// </summary>
    private Task<AgentToolAuthorizationResult> EvaluateExplicitPolicy(
        AgentToolPermissionRequirement permission,
        string expectedToolName)
    {
        // If explicitly allowed, grant access
        if (_options.AllowedPermissions.Contains(permission.PermissionName) ||
            _options.AllowedToolNames.Contains(expectedToolName))
        {
            return Task.FromResult(AgentToolAuthorizationResult.Allowed());
        }

        // Not explicitly allowed — check category defaults
        var category = permission.ToolCategory;

        // Activation handoff tools: controlled by AllowActivationHandoffToolsByDefault
        if (category == AgentToolCategory.ActivationHandoff)
        {
            if (!_options.AllowActivationHandoffToolsByDefault)
            {
                return Task.FromResult(AgentToolAuthorizationResult.Denied(
                    new AgentToolDiagnostic
                    {
                        Code = "ACTIVATION_HANDOFF_DENIED",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Activation handoff tool '{expectedToolName}' is not allowed by default. Enable AllowActivationHandoffToolsByDefault or add to AllowedPermissions/AllowedToolNames."
                    }));
            }

            return Task.FromResult(AgentToolAuthorizationResult.Allowed());
        }

        // Mutating tools (IsReadOnly = false): controlled by AllowMutationToolsByDefault
        if (!permission.IsReadOnly)
        {
            if (!_options.AllowMutationToolsByDefault)
            {
                return Task.FromResult(AgentToolAuthorizationResult.Denied(
                    new AgentToolDiagnostic
                    {
                        Code = "MUTATION_DENIED",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Mutating tool '{expectedToolName}' is not allowed by default. Enable AllowMutationToolsByDefault or add to AllowedPermissions/AllowedToolNames."
                    }));
            }

            return Task.FromResult(AgentToolAuthorizationResult.Allowed());
        }

        // Read-only tools: controlled by AllowReadOnlyToolsByDefault
        if (permission.IsReadOnly)
        {
            if (!_options.AllowReadOnlyToolsByDefault)
            {
                return Task.FromResult(AgentToolAuthorizationResult.Denied(
                    new AgentToolDiagnostic
                    {
                        Code = "READ_ONLY_DENIED",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Read-only tool '{expectedToolName}' is not allowed by default. Enable AllowReadOnlyToolsByDefault or add to AllowedPermissions/AllowedToolNames."
                    }));
            }

            return Task.FromResult(AgentToolAuthorizationResult.Allowed());
        }

        // Should not reach here, but fail-closed
        return Task.FromResult(AgentToolAuthorizationResult.Denied(
            new AgentToolDiagnostic
            {
                Code = "AUTHORIZATION_UNRESOLVED",
                Severity = AgentToolDiagnosticSeverity.Blocker,
                Message = $"Authorization could not be resolved for tool '{expectedToolName}'."
            }));
    }

    /// <summary>
    /// Converts a legacy <see cref="AgentToolAuthorizationPolicy"/> to equivalent
    /// <see cref="AgentToolAuthorizationOptions"/>.
    /// </summary>
    private static AgentToolAuthorizationOptions PolicyToOptions(AgentToolAuthorizationPolicy policy)
    {
        // Legacy policy → ExplicitPolicy with deny lists forwarded.
        // An empty policy is no longer implicitly DevelopmentAllowAll because that
        // would allow mutating tools without explicit opt-in. Callers who want
        // DevelopmentAllowAll must use AgentToolAuthorizationOptions.DevelopmentDefaults.
        return new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = false,
            AllowActivationHandoffToolsByDefault = false,
            DeniedPermissions = policy.DeniedPermissionNames,
            DeniedDescriptorKinds = policy.DeniedDescriptorKinds,
            DeniedToolNames = policy.DeniedToolNames,
            DeniedActorKinds = policy.DeniedActorKinds
        };
    }
}
