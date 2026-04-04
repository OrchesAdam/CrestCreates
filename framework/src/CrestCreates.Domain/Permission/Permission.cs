using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class Permission : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public Guid? ParentId { get; set; }
    public string? GroupName { get; set; }
    public bool IsEnabled { get; set; } = true;
}
