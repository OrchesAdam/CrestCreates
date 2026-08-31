namespace CrestCreates.Organization.Abstractions;

public sealed class OrganizationHierarchyFreshnessException : OrganizationException
{
    public OrganizationHierarchyFreshnessFailureKind FailureKind { get; }

    public long? ObservedGeneration { get; }

    public long? ObservedHighWaterGeneration { get; }

    public long? QuarantineFloorGeneration { get; }

    public OrganizationHierarchyFreshnessException(
        OrganizationHierarchyFreshnessFailureKind failureKind,
        long? observedGeneration = null,
        long? observedHighWaterGeneration = null,
        long? quarantineFloorGeneration = null,
        string? message = null)
        : base(message ?? BuildMessage(failureKind, observedGeneration, observedHighWaterGeneration, quarantineFloorGeneration))
    {
        FailureKind = failureKind;
        ObservedGeneration = observedGeneration;
        ObservedHighWaterGeneration = observedHighWaterGeneration;
        QuarantineFloorGeneration = quarantineFloorGeneration;
    }

    private static string BuildMessage(
        OrganizationHierarchyFreshnessFailureKind failureKind,
        long? observedGeneration,
        long? observedHighWaterGeneration,
        long? quarantineFloorGeneration)
    {
        var part = $"Freshness failure: {failureKind}";
        if (observedGeneration.HasValue)
            part += $", observed={observedGeneration}";
        if (observedHighWaterGeneration.HasValue)
            part += $", highWater={observedHighWaterGeneration}";
        if (quarantineFloorGeneration.HasValue)
            part += $", quarantineFloor={quarantineFloorGeneration}";
        return part;
    }
}
