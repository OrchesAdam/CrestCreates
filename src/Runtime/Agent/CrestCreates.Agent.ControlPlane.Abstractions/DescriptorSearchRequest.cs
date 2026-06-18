using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorSearchRequest
{
    public string? Namespace { get; init; }
    public DescriptorKind? Kind { get; init; }
    public string? NameContains { get; init; }
    public DescriptorState? State { get; init; }
    public int MaxResults { get; init; } = 50;
}
