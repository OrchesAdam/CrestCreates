namespace CrestCreates.Organization;

internal sealed class OrganizationHierarchyScopeState
{
    private readonly object _gate = new();

    public string ScopeKey { get; }
    public OrganizationHierarchySafetyMode Mode { get; private set; }
    public long? ObservedHighWater { get; private set; }
    public long? QuarantineFloor { get; private set; }
    public long Revision { get; private set; }

    public object Gate => _gate;

    public OrganizationHierarchyScopeState(string scopeKey, long revision)
    {
        ScopeKey = scopeKey;
        Mode = OrganizationHierarchySafetyMode.Normal;
        Revision = revision;
    }

    public void Update(
        OrganizationHierarchySafetyMode mode,
        long? observedHighWater,
        long? quarantineFloor,
        long revision)
    {
        Mode = mode;
        ObservedHighWater = observedHighWater;
        QuarantineFloor = quarantineFloor;
        Revision = revision;
    }
}
