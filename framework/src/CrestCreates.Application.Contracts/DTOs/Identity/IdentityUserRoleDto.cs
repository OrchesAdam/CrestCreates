using System;

namespace CrestCreates.Application.Contracts.DTOs.Identity;

public class IdentityUserRoleDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
