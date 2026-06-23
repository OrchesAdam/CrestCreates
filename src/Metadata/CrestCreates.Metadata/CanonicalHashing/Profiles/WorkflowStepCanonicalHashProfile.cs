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
/// Target is included via <see cref="CustomWriter"/> (<see cref="InteractionTargetCanonicalHashWriter"/>)
/// for discriminated union serialization.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(WorkflowStep),
    ContractShapeVersion = "workflow-step-contract-hash-v1",
    DefinitionShapeVersion = "workflow-step-definition-hash-v1")]
internal sealed class WorkflowStepCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(WorkflowStep.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(WorkflowStep.Condition), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(WorkflowStep.InputMapping), CanonicalHashFieldClassification.Contract, Order = 30)]
    [CanonicalHashField(nameof(WorkflowStep.OutputMapping), CanonicalHashFieldClassification.Contract, Order = 40)]
    [CanonicalHashField(nameof(WorkflowStep.Transitions), CanonicalHashFieldClassification.Contract, Order = 50,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(WorkflowStep.Name), CanonicalHashFieldClassification.DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(WorkflowStep.OnError), CanonicalHashFieldClassification.DefinitionOnly, Order = 110)]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(WorkflowStep.Target), CanonicalHashFieldClassification.Contract, Order = 10,
        CustomWriter = typeof(InteractionTargetCanonicalHashWriter),
        Reason = "Discriminant union — hand-written InteractionTargetCanonicalHashWriter")]

    private static void Fields() { }
}
