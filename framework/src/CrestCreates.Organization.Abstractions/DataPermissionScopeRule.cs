namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScopeRule
{
    public required string Resource { get; init; }
    public string? Action { get; init; }
    public string? Permission { get; init; }
    public string? TenantId { get; init; }
    public DataPermissionScopeKind ScopeKind { get; init; }
}
