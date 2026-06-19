namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Authorization options for the Agent Control Plane tool surface.
/// Provides explicit, mode-driven authorization configuration.
///
/// <para>Authorization semantics:</para>
/// <list type="bullet">
///   <item><see cref="Mode"/> determines the default stance (allow-all, explicit, deny-all)</item>
///   <item>In <see cref="AgentToolAuthorizationMode.ExplicitPolicy"/> mode, tool categories
///         are controlled by <see cref="AllowReadOnlyToolsByDefault"/>,
///         <see cref="AllowMutationToolsByDefault"/>,
///         <see cref="AllowActivationHandoffToolsByDefault"/></item>
///   <item><see cref="AllowedPermissions"/> and <see cref="AllowedToolNames"/> grant
///         explicit access regardless of category defaults</item>
///   <item><see cref="DeniedPermissions"/> and <see cref="DeniedToolNames"/> always
///         override allow rules (deny wins)</item>
///   <item>Runtime execution tools (<c>agent.runtime.*</c>) are always denied
///         regardless of configuration</item>
/// </list>
/// </summary>
public sealed record AgentToolAuthorizationOptions
{
    /// <summary>
    /// Authorization mode controlling the default stance.
    /// Default is <see cref="AgentToolAuthorizationMode.ExplicitPolicy"/> for production safety.
    /// </summary>
    public AgentToolAuthorizationMode Mode { get; init; } = AgentToolAuthorizationMode.ExplicitPolicy;

    /// <summary>
    /// Permission names that are explicitly allowed, regardless of category defaults.
    /// In <see cref="AgentToolAuthorizationMode.ExplicitPolicy"/> mode, a mutating or
    /// activation handoff tool must have its permission listed here (or its tool name
    /// in <see cref="AllowedToolNames"/>) to be invoked.
    /// </summary>
    public HashSet<string> AllowedPermissions { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Permission names that are always denied, overriding any allow rule.
    /// Deny always wins.
    /// </summary>
    public HashSet<string> DeniedPermissions { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Tool names that are explicitly allowed, regardless of category defaults.
    /// Grants access to the tool by name even if its permission category is not
    /// enabled by default.
    /// </summary>
    public HashSet<string> AllowedToolNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Tool names that are always denied, overriding any allow rule.
    /// Deny always wins.
    /// </summary>
    public HashSet<string> DeniedToolNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Descriptor kinds that are always denied.
    /// </summary>
    public HashSet<string> DeniedDescriptorKinds { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Actor kinds that are always denied.
    /// </summary>
    public HashSet<AgentToolActorKind> DeniedActorKinds { get; init; } = new();

    /// <summary>
    /// In <see cref="AgentToolAuthorizationMode.ExplicitPolicy"/> mode, allows read-only
    /// and context tools without requiring explicit permission grants.
    /// Default is <c>true</c> — read/context tools are safe to enable by default.
    /// </summary>
    public bool AllowReadOnlyToolsByDefault { get; init; } = true;

    /// <summary>
    /// In <see cref="AgentToolAuthorizationMode.ExplicitPolicy"/> mode, allows mutating
    /// tools (draft create/update/cancel, fix apply, review run) without requiring
    /// explicit permission grants.
    /// Default is <c>false</c> — mutating tools must be explicitly allowed.
    /// </summary>
    public bool AllowMutationToolsByDefault { get; init; } = false;

    /// <summary>
    /// In <see cref="AgentToolAuthorizationMode.ExplicitPolicy"/> mode, allows activation
    /// handoff tools (submit/cancel activation request) without requiring explicit
    /// permission grants.
    /// Default is <c>false</c> — activation handoff tools must be explicitly allowed.
    /// </summary>
    public bool AllowActivationHandoffToolsByDefault { get; init; } = false;

    // ── Static factory helpers ──

    /// <summary>
    /// Options that allow all tools. Suitable for development and test environments only.
    /// Runtime execution tools are still denied by the authorization service.
    /// </summary>
    public static AgentToolAuthorizationOptions DevelopmentDefaults => new()
    {
        Mode = AgentToolAuthorizationMode.DevelopmentAllowAll
    };

    /// <summary>
    /// Production-safe defaults: read-only/context tools allowed, mutating and
    /// activation handoff tools require explicit permission grants.
    /// This is the framework default for <see cref="AgentToolAuthorizationMode.ExplicitPolicy"/>.
    /// </summary>
    public static AgentToolAuthorizationOptions ProductionDefaults => new()
    {
        Mode = AgentToolAuthorizationMode.ExplicitPolicy,
        AllowReadOnlyToolsByDefault = true,
        AllowMutationToolsByDefault = false,
        AllowActivationHandoffToolsByDefault = false
    };

    /// <summary>
    /// Maximum lockdown: all tools denied unless explicitly allowed.
    /// </summary>
    public static AgentToolAuthorizationOptions LockedDown => new()
    {
        Mode = AgentToolAuthorizationMode.DenyAll
    };
}
