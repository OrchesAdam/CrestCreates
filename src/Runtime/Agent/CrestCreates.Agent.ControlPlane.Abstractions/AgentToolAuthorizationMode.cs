namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Authorization mode for the Agent Control Plane tool surface.
/// Controls the default stance when no explicit allow/deny rule matches.
/// </summary>
public enum AgentToolAuthorizationMode
{
    /// <summary>
    /// All tools are allowed unless explicitly denied.
    /// Suitable for development, testing, and local demos only.
    /// Runtime execution tools are still denied by the authorization service
    /// regardless of mode.
    /// </summary>
    DevelopmentAllowAll = 0,

    /// <summary>
    /// Tools are allowed or denied based on explicit configuration.
    /// Read-only tools may be allowed by default via <see cref="AgentToolAuthorizationOptions.AllowReadOnlyToolsByDefault"/>.
    /// Mutating and activation handoff tools require explicit permission grants.
    /// Denied lists override allowed lists.
    /// </summary>
    ExplicitPolicy = 1,

    /// <summary>
    /// All tools are denied unless explicitly allowed.
    /// Maximum lockdown; every tool invocation must be explicitly granted.
    /// </summary>
    DenyAll = 2
}
