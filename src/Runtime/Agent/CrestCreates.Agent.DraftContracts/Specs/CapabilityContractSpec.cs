using CrestCreates.Metadata;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.DraftContracts.Specs;

[AgentDraftContractSpec(Kind = DescriptorKind.Capability)]
internal sealed class CapabilityContractSpec
{
    // ── EditableScalar ──
    [AgentDraftField]
    public string Name { get; init; } = string.Empty;

    [AgentDraftField]
    public DescriptorState State { get; init; }

    [AgentDraftField]
    public CapabilityKind CapabilityKind { get; init; }

    [AgentDraftField]
    public CapabilityRiskLevel RiskLevel { get; init; }

    [AgentDraftField]
    public string ContractHash { get; init; } = string.Empty;

    [AgentDraftField]
    public string DefinitionHash { get; init; } = string.Empty;

    [AgentDraftField]
    public int Version { get; init; }

    // ── EditableReference ──
    [AgentDraftReference]
    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }

    [AgentDraftReference]
    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }

    [AgentDraftReference]
    [AgentDraftContractName(Name = "Produces")]
    public IReadOnlyList<EventRef> Produces { get; init; } = [];

    [AgentDraftReference]
    [AgentDraftContractName(Name = "Consumes")]
    public IReadOnlyList<EventRef> Consumes { get; init; } = [];

    // ── Preserve ──
    [AgentDraftPreserve(Reason = "SupersededById is managed by the descriptor lifecycle, not by agent draft editing.", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
    public string? SupersededById { get; init; }

    // ── Unsupported ──
    [AgentDraftUnsupported(Reason = "Categories is a classification tag collection that requires dedicated tooling for structured editing.")]
    public IReadOnlyList<string> Categories { get; init; } = [];

    [AgentDraftUnsupported(Reason = "SemanticTags is a classification tag collection that requires dedicated tooling for structured editing.")]
    public IReadOnlyList<string> SemanticTags { get; init; } = [];

    [AgentDraftUnsupported(Reason = "Permissions is an authorization collection managed through the permission management system, not draft editing.")]
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
