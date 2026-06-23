using CrestCreates.Form.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.DraftContracts.Specs;

[AgentDraftContractSpec(Kind = DescriptorKind.Form)]
internal sealed class FormContractSpec
{
    // ── EditableScalar ──
    [AgentDraftField]
    public string Name { get; init; } = string.Empty;

    [AgentDraftField]
    public DescriptorState State { get; init; }

    [AgentDraftField]
    public int Version { get; init; }

    // ── EditableReference ──
    [AgentDraftReference]
    [AgentDraftContractName(Name = "FormSchema")]
    [AgentDraftRequiredOnCreate]
    public VersionedDescriptorRef<SchemaDescriptor> Schema { get; init; }

    // ── Preserve ──
    [AgentDraftPreserve(Reason = "Fields are form field configuration managed by dedicated form editing tools.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public IReadOnlyList<FormFieldDescriptor> Fields { get; init; } = [];

    [AgentDraftPreserve(Reason = "LayoutColumns is form layout configuration managed by dedicated form editing tools.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? LayoutColumns { get; init; }

    [AgentDraftPreserve(Reason = "SupersededById is managed by the descriptor lifecycle, not by agent draft editing.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? SupersededById { get; init; }
}
