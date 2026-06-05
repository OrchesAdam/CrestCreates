using System;
using System.Collections.Generic;

namespace CrestCreates.MultiTenancy.Abstract;

public class TenantConfiguration
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ConnectionString { get; set; }
    public bool IsActive { get; set; }
    public Dictionary<string, string> ConnectionStrings { get; set; } = new();
}
