using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringPromptOutput : ISnapshotable<DescriptorAuthoringPromptOutput>
{
    public required string ContractVersion { get; init; }
    public required string PromptTemplateVersion { get; init; }
    public required CanonicalHash PromptInputHash { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }

    public DescriptorAuthoringPromptOutput Snapshot() => this;
}
