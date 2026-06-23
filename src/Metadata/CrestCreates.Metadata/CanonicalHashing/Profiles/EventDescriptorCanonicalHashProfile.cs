using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="EventDescriptor"/>.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Name, Version, State, SupersededById,
///   PayloadSchema, Category, Semantic, ChangeKind
///
/// DefinitionOnly fields (only in DefinitionHash):
///   Importance
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Event,
    TargetType = typeof(EventDescriptor),
    ContractShapeVersion = "event-contract-hash-v1",
    DefinitionShapeVersion = "event-definition-hash-v1")]
internal sealed class EventDescriptorCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(EventDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(EventDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(EventDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(EventDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(EventDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(EventDescriptor.PayloadSchema), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedSchemaRefCanonicalHashProfile))]
    [CanonicalHashField(nameof(EventDescriptor.Category), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(EventDescriptor.Semantic), CanonicalHashFieldClassification.Contract, Order = 30)]
    [CanonicalHashField(nameof(EventDescriptor.ChangeKind), CanonicalHashFieldClassification.Contract, Order = 40)]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(EventDescriptor.Importance), CanonicalHashFieldClassification.DefinitionOnly, Order = 110)]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(EventDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(EventDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}
