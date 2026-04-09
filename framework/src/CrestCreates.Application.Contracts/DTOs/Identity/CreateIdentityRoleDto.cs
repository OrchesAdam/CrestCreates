using System;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Application.Contracts.DTOs.Identity;

public class CreateIdentityRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public DataScope DataScope { get; set; } = DataScope.Self;
}
