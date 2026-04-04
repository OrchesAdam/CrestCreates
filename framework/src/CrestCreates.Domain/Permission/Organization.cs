using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class Organization : MustHaveTenantOrganizationEntity<Guid>
{
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public string? Path { get; set; }
    public bool IsActive { get; set; } = true;
}
