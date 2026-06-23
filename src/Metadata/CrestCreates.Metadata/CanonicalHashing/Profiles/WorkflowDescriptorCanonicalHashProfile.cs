using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="WorkflowDescriptor"/>.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Name, Version, State, SupersededById,
///   VariableSchema, DefaultVariableScope, Steps
///
/// All WorkflowDescriptor fields are Contract — no DefinitionOnly fields.
/// Steps use <see cref="CanonicalHashCollectionOrderMode.SourceOrder"/>
/// because step order is semantically meaningful.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Workflow,
    TargetType = typeof(WorkflowDescriptor),
    ContractShapeVersion = "workflow-contract-hash-v1",
    DefinitionShapeVersion = "workflow-definition-hash-v1")]
internal sealed class WorkflowDescriptorCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(WorkflowDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(WorkflowDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(WorkflowDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(WorkflowDescriptor.VariableSchema), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedSchemaRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(WorkflowDescriptor.DefaultVariableScope), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(WorkflowDescriptor.Steps), CanonicalHashFieldClassification.Contract, Order = 30,
        ElementProfile = typeof(WorkflowStepCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.SourceOrder)]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(WorkflowDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(WorkflowDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}
