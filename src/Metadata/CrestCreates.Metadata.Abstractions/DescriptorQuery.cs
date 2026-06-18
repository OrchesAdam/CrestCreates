namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorQuery
{
    public string? ContractHash { get; init; }
    public IReadOnlyList<string>? SemanticTags { get; init; }
    public IReadOnlyList<string>? Categories { get; init; }
    public string? Namespace { get; init; }
}
