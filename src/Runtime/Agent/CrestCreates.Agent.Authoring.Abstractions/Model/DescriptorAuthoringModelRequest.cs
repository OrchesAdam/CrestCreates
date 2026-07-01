using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringModelRequest : ISnapshotable<DescriptorAuthoringModelRequest>
{
    public required DescriptorAuthoringPromptOutput Prompt { get; init; }
    public required DescriptorAuthoringModelProfile ModelProfile { get; init; }

    public DescriptorAuthoringModelRequest Snapshot() => this with
    {
        Prompt = Prompt.Snapshot(),
        ModelProfile = ModelProfile.Snapshot()
    };
}
