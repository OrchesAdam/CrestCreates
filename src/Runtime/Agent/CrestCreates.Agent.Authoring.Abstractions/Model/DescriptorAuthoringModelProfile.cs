using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringModelProfile : ISnapshotable<DescriptorAuthoringModelProfile>
{
    public required string ProfileName { get; init; }
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public int? MaxInputTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public bool SupportsJsonMode { get; init; }
    public bool SupportsStructuredOutput { get; init; }

    public DescriptorAuthoringModelProfile Snapshot() => this;
}
