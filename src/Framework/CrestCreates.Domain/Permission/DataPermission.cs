using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Domain.Permission;

public class DataPermission : Entity<Guid>
{
    public Guid RoleId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public DataScope DataScope { get; set; }
    public string? CustomFilter { get; set; }
    public string? TenantId { get; set; }
}
