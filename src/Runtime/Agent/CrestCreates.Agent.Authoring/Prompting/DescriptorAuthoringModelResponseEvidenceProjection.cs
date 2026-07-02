using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed record DescriptorAuthoringModelResponseEvidenceProjection
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public CanonicalHash? PromptInputHash { get; init; }
    public DescriptorAuthoringProviderFailureKind FailureKind { get; init; }
    public string? FailureDetail { get; init; }
}
