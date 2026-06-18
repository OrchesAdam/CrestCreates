using System;

namespace CrestCreates.Application.Contracts.DTOs.Identity;

public class IdentityUserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public bool IsActive { get; set; }
    public bool IsSuperAdmin { get; set; }
    public int AccessFailedCount { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime? LockoutEndTime { get; set; }
}
