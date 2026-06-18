using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Configurable authorization policy for the Agent Control Plane tool surface.
/// </summary>
public sealed record AgentToolAuthorizationPolicy
{
    public HashSet<string> DeniedPermissionNames { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> DeniedDescriptorKinds { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> DeniedToolNames { get; init; } = new(StringComparer.Ordinal);
    public HashSet<AgentToolActorKind> DeniedActorKinds { get; init; } = new();

    /// <summary>
    /// Default policy that allows all Control Plane permissions.
    /// Runtime execution permissions are still denied by the authorization service
    /// regardless of policy configuration.
    /// </summary>
    public static AgentToolAuthorizationPolicy AllowAll => new();

    /// <summary>
    /// Policy that denies all activation request submission permissions,
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
}
