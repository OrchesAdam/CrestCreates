namespace CrestCreates.Organization;

internal enum OrganizationHierarchySafetyMode
{
    Normal = 0,
    Quarantined = 1
}

internal sealed record OrganizationHierarchySafetyState(
    OrganizationHierarchySafetyMode Mode,
    long? ObservedHighWater,
    long? QuarantineFloor,
    long Revision);

internal readonly record struct OrganizationHierarchyAdmissionToken(
    string ScopeKey,
    long Revision,
    OrganizationHierarchySafetyMode Mode,
    long? ObservedHighWater,
    long? QuarantineFloor,
    long? Generation);
