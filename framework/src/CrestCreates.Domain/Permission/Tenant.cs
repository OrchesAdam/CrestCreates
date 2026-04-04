using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class Tenant : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? ConnectionString { get; set; }
    public bool IsActive { get; set; } = true;
}
