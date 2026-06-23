using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.DraftContracts.Specs;

[AgentDraftContractSpec(Kind = DescriptorKind.HumanTask)]
internal sealed class HumanTaskContractSpec
{
    // ── EditableScalar ──
    [AgentDraftField]
    public string Name { get; init; } = string.Empty;

    [AgentDraftField]
    public DescriptorState State { get; init; }

    [AgentDraftField]
    public AssigneeStrategy AssigneeStrategy { get; init; }

    [AgentDraftField]
    public int Version { get; init; }

    [AgentDraftField]
    public TimeSpan? Timeout { get; init; }

    // ── EditableReference ──
    [AgentDraftReference]
    [AgentDraftRequiredOnCreate]
    public VersionedDescriptorRef<IInteractionDescriptor> Interaction { get; init; }

    [AgentDraftReference]
    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }

    [AgentDraftReference]
    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }

    // ── Preserve ──
    [AgentDraftPreserve(Reason = "Permissions is an authorization string managed through the permission system.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? Permissions { get; init; }

    [AgentDraftPreserve(Reason = "Outcomes are human task completion configuration managed by dedicated tools.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public IReadOnlyList<CompletionOutcome> Outcomes { get; init; } = [];

    [AgentDraftPreserve(Reason = "SupersededById is managed by the descriptor lifecycle, not by agent draft editing.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? SupersededById { get; init; }
}
