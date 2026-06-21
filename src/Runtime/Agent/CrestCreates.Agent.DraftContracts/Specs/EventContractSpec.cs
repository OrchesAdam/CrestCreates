using CrestCreates.Event.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.DraftContracts.Specs;

[AgentDraftContractSpec(Kind = DescriptorKind.Event)]
internal sealed class EventContractSpec
{
    // ── EditableScalar ──
    [AgentDraftField]
    public string Name { get; init; } = string.Empty;

    [AgentDraftField]
    public DescriptorState State { get; init; }

    [AgentDraftField]
    public EventCategory Category { get; init; }

    [AgentDraftField]
    public EventSemantic Semantic { get; init; }

    [AgentDraftField]
    public EventImportance Importance { get; init; }

    [AgentDraftField]
    public SchemaChangeKind ChangeKind { get; init; }

    [AgentDraftField]
    public string ContractHash { get; init; } = string.Empty;

    [AgentDraftField]
    public string DefinitionHash { get; init; } = string.Empty;

    [AgentDraftField]
    public int Version { get; init; }

    // ── EditableReference ──
    [AgentDraftReference]
    [AgentDraftRequiredOnCreate]
    public VersionedDescriptorRef<SchemaDescriptor> PayloadSchema { get; init; }

    // ── Preserve ──
    [AgentDraftPreserve(Reason = "SupersededById is managed by the descriptor lifecycle, not by agent draft editing.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? SupersededById { get; init; }
}
