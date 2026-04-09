using System;

namespace CrestCreates.Application.Contracts.DTOs.Identity;

public class UpdateIdentityUserDto
{
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public Guid? OrganizationId { get; set; }
    public bool IsSuperAdmin { get; set; }
}
