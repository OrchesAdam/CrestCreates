namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScopeRequest
{
    public required string UserId { get; init; }
    public string? TenantId { get; init; }
    public string? Permission { get; init; }
    public string? Resource { get; init; }
    public string? Action { get; init; }
}
