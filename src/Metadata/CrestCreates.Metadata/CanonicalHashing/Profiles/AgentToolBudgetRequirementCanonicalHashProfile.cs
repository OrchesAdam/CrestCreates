using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(AgentToolBudgetRequirement),
    ContractShapeVersion = "agent-tool-budget-requirement-hash-v1",
    DefinitionShapeVersion = "agent-tool-budget-requirement-hash-v1")]
internal sealed class AgentToolBudgetRequirementCanonicalHashProfile
{
    [CanonicalHashField(nameof(AgentToolBudgetRequirement.Category), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(AgentToolBudgetRequirement.CostUnits), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(AgentToolBudgetRequirement.MaxCallsPerExecution), CanonicalHashFieldClassification.Contract, Order = 2)]
    private static void Fields() { }
}
