using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorChange
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorChangeKind Kind { get; init; }
    public DescriptorState? BeforeState { get; init; }
    public DescriptorState? AfterState { get; init; }
    public string? BeforeContractHash { get; init; }
    public string? AfterContractHash { get; init; }
    public string? BeforeDefinitionHash { get; init; }
    public string? AfterDefinitionHash { get; init; }
    public string? Reason { get; init; }
}
