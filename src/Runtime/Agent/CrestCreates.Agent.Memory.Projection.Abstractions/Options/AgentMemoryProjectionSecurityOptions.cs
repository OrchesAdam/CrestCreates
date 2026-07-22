namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Configuration for projection security infrastructure.
/// TimeProvider is a Host dependency — Projection does not auto-register it.
/// </summary>
public sealed class AgentMemoryProjectionSecurityOptions
{
    public TimeSpan McpInvocationProvisionalLifetime { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan McpSessionLifetimeCap { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan CompensationTokenTrackingLifetime { get; set; } = TimeSpan.FromMinutes(2);
}
