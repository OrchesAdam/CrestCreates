using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

// CCHASH009: CapabilityEndpointDescriptor does not follow the TypeName = DescriptorKind + "Descriptor" convention.
// The type was intentionally named to reflect that it is a Capability projection, not a standalone DynamicApiEndpoint.
#pragma warning disable CCHASH009

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="CapabilityEndpointDescriptor"/>.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   Id, Name, Version, State, SupersededById,
///   Capability (value profile), HttpMethod, RoutePattern, AuthorizationMode,
///   InputBindings (element profile), OutputMapping (value profile),
///   Projection (value profile — OperationId is contract within projection)
///
/// Excluded fields:
///   Namespace, Kind (runtime constants)
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.DynamicApiEndpoint,
    TargetType = typeof(CapabilityEndpointDescriptor),
    ContractShapeVersion = "dynamic-api-endpoint-contract-hash-v1",
    DefinitionShapeVersion = "dynamic-api-endpoint-definition-hash-v1")]
internal sealed class CapabilityEndpointDescriptorCanonicalHashProfile
{
    // ── Contract fields ──

    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Capability), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedDescriptorRefCapabilityCanonicalHashProfile))]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.HttpMethod), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.RoutePattern), CanonicalHashFieldClassification.Contract, Order = 21)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.AuthorizationMode), CanonicalHashFieldClassification.Contract, Order = 22)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.InputBindings), CanonicalHashFieldClassification.Contract, Order = 30,
        ElementProfile = typeof(CapabilityEndpointInputBindingCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Source,Name,CapabilityInputPath")]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.OutputMapping), CanonicalHashFieldClassification.Contract, Order = 40,
        ValueProfile = typeof(CapabilityEndpointOutputMappingCanonicalHashProfile))]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Projection), CanonicalHashFieldClassification.Contract, Order = 50,
        ValueProfile = typeof(CapabilityEndpointProjectionMetadataCanonicalHashProfile))]

    // ── Excluded fields ──

    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant — not part of hash")]
    private static void Fields() { }
}
#pragma warning restore CCHASH009
