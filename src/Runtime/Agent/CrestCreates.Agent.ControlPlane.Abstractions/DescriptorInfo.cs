using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorInfo
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public DescriptorStableHashes? Hashes { get; init; }
}
