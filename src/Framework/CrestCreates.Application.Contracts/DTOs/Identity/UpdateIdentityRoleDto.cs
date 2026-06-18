using System;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Application.Contracts.DTOs.Identity;

public class UpdateIdentityRoleDto
{
    public string? DisplayName { get; set; }
    public Guid? OrganizationId { get; set; }
    public DataScope DataScope { get; set; } = DataScope.Self;
}
