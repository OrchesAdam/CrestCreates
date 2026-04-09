using System;

namespace CrestCreates.Application.Contracts.DTOs.Identity;

public class CreateIdentityUserDto
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public bool IsSuperAdmin { get; set; }
}
