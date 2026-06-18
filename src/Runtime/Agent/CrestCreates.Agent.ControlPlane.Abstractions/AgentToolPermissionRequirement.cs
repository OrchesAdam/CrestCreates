namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolPermissionRequirement
{
    public required string PermissionName { get; init; }
    public string? DescriptorKindConstraint { get; init; }
    public string? Description { get; init; }
}
