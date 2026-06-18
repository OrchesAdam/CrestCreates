namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Control Plane tool manifest descriptor.
/// Distinct from CrestCreates.Exposure.Abstractions.AgentToolDescriptor
/// which maps tools to runtime capabilities.
/// This descriptor describes tool permissions and audit requirements
/// for the Control Plane tool surface.
/// </summary>
public sealed record AgentToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required AgentToolCategory Category { get; init; }
    public required IReadOnlyList<AgentToolPermissionRequirement> Permissions { get; init; }
    public required IReadOnlyList<AgentToolActorKind> AllowedActors { get; init; }
    public bool IsReadOnly { get; init; }
    public bool MutatesRuntimeRegistry { get; init; }
}
