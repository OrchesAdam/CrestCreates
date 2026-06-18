using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class TenantConnectionString : Entity<Guid>
{
    public const string DefaultName = "Default";

    public Guid TenantId { get; set; }
    public string Name { get; private set; } = DefaultName;
    public string Value { get; private set; } = string.Empty;

    public TenantConnectionString()
    {
    }

    public TenantConnectionString(Guid id, Guid tenantId, string name, string value)
    {
        Id = id;
        TenantId = tenantId;
        SetName(name);
        SetValue(value);
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("连接串名称不能为空", nameof(name));
        }

        Name = name.Trim();
    }

    public void SetValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("连接串不能为空", nameof(value));
        }

        Value = value.Trim();
    }
}
