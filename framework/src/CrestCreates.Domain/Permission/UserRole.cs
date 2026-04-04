using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class UserRole : Entity<Guid>
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public string? TenantId { get; set; }
}
