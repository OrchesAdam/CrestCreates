using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="SubWorkflowTarget"/>.
/// Case profile used by <see cref="InteractionTargetCanonicalHashProfile"/> union.
///
/// Contract field: <see cref="SubWorkflowTarget.SubWorkflow"/> (VersionedDescriptorRef&lt;WorkflowDescriptor&gt;)
/// with value profile <see cref="VersionedDescriptorRefSubWorkflowCanonicalHashProfile"/>.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(SubWorkflowTarget),
    ContractShapeVersion = "subworkflow-target-contract-hash-v1",
    DefinitionShapeVersion = "subworkflow-target-definition-hash-v1")]
internal sealed class SubWorkflowTargetCanonicalHashProfile
{
    [CanonicalHashField(nameof(SubWorkflowTarget.SubWorkflow), CanonicalHashFieldClassification.Contract, Order = 0,
        ValueProfile = typeof(VersionedDescriptorRefSubWorkflowCanonicalHashProfile))]
    private static void Fields() { }
}
