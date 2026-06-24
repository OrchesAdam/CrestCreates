using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash union profile for <see cref="InteractionTarget"/> discriminated union.
///
/// Discriminator is "Kind" with three cases:
///   - <see cref="CapabilityTarget"/> → discriminator "Capability"
///   - <see cref="HumanTaskTarget"/>  → discriminator "HumanTask"
///   - <see cref="SubWorkflowTarget"/> → discriminator "Workflow"
///
/// The source generator produces <c>InteractionTargetCanonicalHashWriter</c> with
/// switch-based dispatch that writes "Kind" before "Value" in canonical JSON.
/// </summary>
[CanonicalHashUnionProfile(TargetType = typeof(InteractionTarget), Discriminator = "Kind")]
[CanonicalHashUnionCase(typeof(CapabilityTarget), "Capability", ValueProfile = typeof(CapabilityTargetCanonicalHashProfile))]
[CanonicalHashUnionCase(typeof(HumanTaskTarget), "HumanTask", ValueProfile = typeof(HumanTaskTargetCanonicalHashProfile))]
[CanonicalHashUnionCase(typeof(SubWorkflowTarget), "Workflow", ValueProfile = typeof(SubWorkflowTargetCanonicalHashProfile))]
internal sealed class InteractionTargetCanonicalHashProfile
{
}
