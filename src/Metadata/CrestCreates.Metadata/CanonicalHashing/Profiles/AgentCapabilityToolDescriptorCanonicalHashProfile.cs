using CrestCreates.Metadata.AgentTool;

// CCHASH009: the approved type name makes its Capability authority explicit and
// intentionally differs from the DescriptorKind + "Descriptor" convention.
#pragma warning disable CCHASH009

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.AgentTool,
    TargetType = typeof(AgentCapabilityToolDescriptor),
    ContractShapeVersion = "agent-tool-contract-hash-v1",
    DefinitionShapeVersion = "agent-tool-definition-hash-v1")]
internal sealed class AgentCapabilityToolDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Capability), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(AgentToolCapabilityProjectionReferenceCanonicalHashProfile))]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.ToolName), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Title), CanonicalHashFieldClassification.Contract, Order = 21)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Description), CanonicalHashFieldClassification.Contract, Order = 22)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.SelectionPolicy), CanonicalHashFieldClassification.Contract, Order = 30)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.SideEffectKind), CanonicalHashFieldClassification.Contract, Order = 31)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.RiskFloor), CanonicalHashFieldClassification.Contract, Order = 32)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.ApprovalMode), CanonicalHashFieldClassification.Contract, Order = 33)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Budget), CanonicalHashFieldClassification.Contract, Order = 34,
        ValueProfile = typeof(AgentToolBudgetRequirementCanonicalHashProfile))]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.AuditMode), CanonicalHashFieldClassification.Contract, Order = 35)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.AllowedAgentRoles), CanonicalHashFieldClassification.Contract, Order = 36,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(AgentCapabilityToolDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}

#pragma warning restore CCHASH009
