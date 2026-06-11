namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionFilter
{
    public bool IsDenied { get; init; }
    public bool IsUnrestricted { get; init; }
    public IReadOnlyList<DataPermissionFilterRule> Rules { get; init; } = Array.Empty<DataPermissionFilterRule>();
}
