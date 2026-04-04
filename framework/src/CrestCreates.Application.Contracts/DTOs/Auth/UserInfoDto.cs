using System;

namespace CrestCreates.Application.Contracts.DTOs.Auth;

public class UserInfoDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? TenantId { get; set; }
    public Guid? OrganizationId { get; set; }
    public bool IsSuperAdmin { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
