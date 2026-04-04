using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Domain.Permission;

public class Role : MustHaveTenantOrganizationEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DataScope DataScope { get; set; } = DataScope.Self;
    public bool IsActive { get; set; } = true;
}
