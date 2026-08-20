using System.Collections.Immutable;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

public sealed record DescriptorPayloadObservation(
    DescriptorPayloadVariant Variant,
    ImmutableArray<DescriptorPayloadObservationLeaf> Leaves);

public sealed record DescriptorPayloadObservationLeaf(
    string Path,
    ObservationValueKind Kind,
    string? Text,
    long? Integer,
    decimal? Decimal,
    bool? Boolean);

public enum ObservationValueKind
{
    Null,
    Text,
    Integer,
    Decimal,
    Boolean,
    EnumUnderlyingValue,
    Ticks
}
