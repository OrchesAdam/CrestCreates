using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Authorization.Abstractions;

public sealed class PermissionGrantInfo
{
    public string PermissionName { get; init; } = string.Empty;
    public PermissionGrantProviderType ProviderType { get; init; }
    public string ProviderKey { get; init; } = string.Empty;
    public PermissionGrantScope Scope { get; init; }
    public string? TenantId { get; init; }
}
