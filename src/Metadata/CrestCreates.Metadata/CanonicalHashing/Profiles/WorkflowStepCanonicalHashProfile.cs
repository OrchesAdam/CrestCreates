using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="WorkflowStep"/>.
/// Sub-structure of <see cref="WorkflowDescriptor"/>, not a standalone descriptor.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Condition, InputMapping, OutputMapping, Transitions
///
/// DefinitionOnly fields (only in DefinitionHash):
///   Name, OnError
///
/// Target is included via <see cref="ValueProfile"/> (<see cref="InteractionTargetCanonicalHashProfile"/>)
/// for discriminated union serialization (Canonical Hash Profile Semantics v2).
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(WorkflowStep),
    ContractShapeVersion = "workflow-step-contract-hash-v2",
    DefinitionShapeVersion = "workflow-step-definition-hash-v2")]
internal sealed class WorkflowStepCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(WorkflowStep.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(WorkflowStep.Condition), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(WorkflowStep.InputMapping), CanonicalHashFieldClassification.Contract, Order = 30)]
    [CanonicalHashField(nameof(WorkflowStep.OutputMapping), CanonicalHashFieldClassification.Contract, Order = 40)]
    [CanonicalHashField(nameof(WorkflowStep.Transitions), CanonicalHashFieldClassification.Contract, Order = 50,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]

    // ── Union value fields ──

    [CanonicalHashField(nameof(WorkflowStep.Target), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(InteractionTargetCanonicalHashProfile))]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(WorkflowStep.Name), CanonicalHashFieldClassification.DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(WorkflowStep.OnError), CanonicalHashFieldClassification.DefinitionOnly, Order = 110)]

    private static void Fields() { }
}
