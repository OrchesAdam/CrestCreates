using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class RolePermission : Entity<Guid>
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public string? TenantId { get; set; }
}
