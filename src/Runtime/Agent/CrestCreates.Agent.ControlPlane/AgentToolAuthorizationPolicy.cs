using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Legacy authorization policy for the Agent Control Plane tool surface.
///
/// <para>This type is superseded by <see cref="AgentToolAuthorizationOptions"/>
/// which provides mode-driven authorization with category-aware defaults.
/// The <see cref="DefaultAgentToolAuthorizationService"/> accepts this policy
/// via a legacy constructor and converts it to equivalent options internally.</para>
///
/// <para>For new code, use <see cref="AgentToolAuthorizationOptions"/> directly:</para>
/// <list type="bullet">
///   <item><see cref="AgentToolAuthorizationOptions.DevelopmentDefaults"/> replaces <see cref="AllowAll"/></item>
///   <item><see cref="AgentToolAuthorizationOptions.ProductionDefaults"/> replaces <see cref="ProductionDefaults"/></item>
///   <item><see cref="AgentToolAuthorizationOptions.LockedDown"/> replaces manual deny-all configuration</item>
/// </list>
/// </summary>
public sealed record AgentToolAuthorizationPolicy
{
    public HashSet<string> DeniedPermissionNames { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> DeniedDescriptorKinds { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> DeniedToolNames { get; init; } = new(StringComparer.Ordinal);
    public HashSet<AgentToolActorKind> DeniedActorKinds { get; init; } = new();

    /// <summary>
    /// Policy that allows all Control Plane permissions.
    /// Equivalent to <see cref="AgentToolAuthorizationOptions.DevelopmentDefaults"/>.
    /// Suitable for development and test environments only.
    /// </summary>
    public static AgentToolAuthorizationPolicy AllowAll => new();

    /// <summary>
    /// Policy that denies all mutation and handoff permissions,
    /// allowing only read operations.
    /// </summary>
    public static AgentToolAuthorizationPolicy ReadOnly => new()
    {
        DeniedPermissionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            AgentToolPermissionName.DraftCreate,
            AgentToolPermissionName.DraftUpdate,
            AgentToolPermissionName.DraftCancel,
            AgentToolPermissionName.ReviewRun,
            AgentToolPermissionName.FixApplyToDraft,
            AgentToolPermissionName.ActivationRequestSubmit,
            AgentToolPermissionName.ActivationRequestCancel
        }
    };

    /// <summary>
    /// Production-safe default policy that denies mutating/handoff tools.
    /// Equivalent to <see cref="AgentToolAuthorizationOptions.ProductionDefaults"/>.
    /// </summary>
    public static AgentToolAuthorizationPolicy ProductionDefaults => new()
    {
        DeniedPermissionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            AgentToolPermissionName.DraftCreate,
            AgentToolPermissionName.DraftUpdate,
            AgentToolPermissionName.DraftCancel,
            AgentToolPermissionName.FixApplyToDraft,
            AgentToolPermissionName.ActivationRequestSubmit,
            AgentToolPermissionName.ActivationRequestCancel
        }
    };
}
