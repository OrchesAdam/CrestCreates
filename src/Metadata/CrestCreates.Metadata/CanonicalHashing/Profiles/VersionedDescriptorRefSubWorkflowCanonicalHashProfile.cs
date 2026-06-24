using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="VersionedDescriptorRef{TDescriptor}"/>
/// specialized for <see cref="WorkflowDescriptor"/>.
///
/// Used by <see cref="SubWorkflowTargetCanonicalHashProfile"/> to hash
/// <see cref="SubWorkflowTarget.SubWorkflow"/>.
///
/// Only <c>Id</c> and <c>Version</c> participate in the hash.
/// <c>SelectionMode</c> and <c>ExpectedContractHash</c> are excluded.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(VersionedDescriptorRef<WorkflowDescriptor>),
    ContractShapeVersion = "descriptorref-subworkflow-hash-v1",
    DefinitionShapeVersion = "descriptorref-subworkflow-hash-v1")]
internal sealed class VersionedDescriptorRefSubWorkflowCanonicalHashProfile
{
    [CanonicalHashField(nameof(VersionedDescriptorRef<WorkflowDescriptor>.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<WorkflowDescriptor>.Version), CanonicalHashFieldClassification.Contract, Order = 1)]

    [CanonicalHashField(nameof(VersionedDescriptorRef<WorkflowDescriptor>.SelectionMode), CanonicalHashFieldClassification.Excluded,
        Reason = "Protocol-level resolution concern — not part of structural hash")]
    [CanonicalHashField(nameof(VersionedDescriptorRef<WorkflowDescriptor>.ExpectedContractHash), CanonicalHashFieldClassification.Excluded,
        Reason = "Resolution-time input — not part of structural hash")]

    private static void Fields() { }
}
