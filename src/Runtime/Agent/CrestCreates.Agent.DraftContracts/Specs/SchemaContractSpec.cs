using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.DraftContracts.Specs;

[AgentDraftContractSpec(Kind = DescriptorKind.Schema)]
internal sealed class SchemaContractSpec
{
    // ── EditableScalar ──
    [AgentDraftField]
    public string Name { get; init; } = string.Empty;

    [AgentDraftField]
    public DescriptorState State { get; init; }

    [AgentDraftField]
    public SchemaChangeKind ChangeKind { get; init; }

    [AgentDraftField]
    public int Version { get; init; }

    // ── Preserve ──
    [AgentDraftPreserve(Reason = "Fields are schema field definitions managed by dedicated schema editing tools.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public IReadOnlyList<SchemaFieldDescriptor> Fields { get; init; } = [];

    [AgentDraftPreserve(Reason = "ValidationRules are schema validation configuration managed by dedicated schema editing tools.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public IReadOnlyList<SchemaValidationRule> ValidationRules { get; init; } = [];

    [AgentDraftPreserve(Reason = "References are schema cross-references managed by dedicated schema editing tools.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public IReadOnlyList<VersionedDescriptorRef<SchemaDescriptor>> References { get; init; } = [];

    [AgentDraftPreserve(Reason = "SupersededById is managed by the descriptor lifecycle, not by agent draft editing.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? SupersededById { get; init; }
}
