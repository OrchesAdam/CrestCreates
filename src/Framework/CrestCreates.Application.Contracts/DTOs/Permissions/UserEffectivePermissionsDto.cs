using System;

namespace CrestCreates.Application.Contracts.DTOs.Permissions;

public class UserEffectivePermissionsDto
{
    public string UserId { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string[] Permissions { get; set; } = Array.Empty<string>();
}
