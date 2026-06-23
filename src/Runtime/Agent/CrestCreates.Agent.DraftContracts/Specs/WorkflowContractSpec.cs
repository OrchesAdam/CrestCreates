using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Agent.DraftContracts.Specs;

[AgentDraftContractSpec(Kind = DescriptorKind.Workflow)]
internal sealed class WorkflowContractSpec
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
    public VersionedDescriptorRef<SchemaDescriptor>? VariableSchema { get; init; }

    // ── Preserve ──
    [AgentDraftPreserve(Reason = "Steps are workflow structure managed by dedicated workflow editing tools, not agent metadata editing.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public IReadOnlyList<WorkflowStep> Steps { get; init; } = [];

    [AgentDraftPreserve(Reason = "DefaultVariableScope is workflow runtime configuration managed by dedicated tools.", CreateStrategy = PreserveCreateStrategy.KnownDomainDefault)]
    public WorkflowVariableScope DefaultVariableScope { get; init; }

    [AgentDraftPreserve(Reason = "SupersededById is managed by the descriptor lifecycle, not by agent draft editing.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? SupersededById { get; init; }
}
