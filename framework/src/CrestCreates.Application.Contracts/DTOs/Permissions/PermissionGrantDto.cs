using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Application.Contracts.DTOs.Permissions;

public class PermissionGrantDto
{
    public string PermissionName { get; set; } = string.Empty;
    public PermissionGrantProviderType ProviderType { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public PermissionGrantScope Scope { get; set; } = PermissionGrantScope.Global;
    public string? TenantId { get; set; }
}
