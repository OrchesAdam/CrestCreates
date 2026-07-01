using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringModelResponse : ISnapshotable<DescriptorAuthoringModelResponse>
{
    public required string ResponseText { get; init; }
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public CanonicalHash? PromptInputHash { get; init; }
    public DescriptorAuthoringProviderFailureKind FailureKind { get; init; }
    public string? FailureDetail { get; init; }

    public DescriptorAuthoringModelResponse Snapshot() => this;
}
