using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Domain.Entities.Auditing;

namespace CrestCreates.Domain.Permission;

public class Tenant : AuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TenantConnectionString> ConnectionStrings { get; set; } = new();

    public Tenant()
    {
    }

    public Tenant(Guid id, string name)
    {
        Id = id;
        SetName(name);
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("租户名称不能为空", nameof(name));
        }

        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
    }

    public string? GetDefaultConnectionString()
    {
        return ConnectionStrings
            .FirstOrDefault(item => item.Name == TenantConnectionString.DefaultName)
            ?.Value;
    }

    public void SetDefaultConnectionString(string? connectionString)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(connectionString)
            ? null
            : connectionString.Trim();

        var existingConnectionString = ConnectionStrings
            .FirstOrDefault(item => item.Name == TenantConnectionString.DefaultName);

        if (normalizedValue == null)
        {
            if (existingConnectionString is not null)
            {
                ConnectionStrings.Remove(existingConnectionString);
            }

            return;
        }

        if (existingConnectionString == null)
        {
            ConnectionStrings.Add(
                new TenantConnectionString(
                    Guid.NewGuid(),
                    Id,
                    TenantConnectionString.DefaultName,
                    normalizedValue));
            return;
        }

        existingConnectionString.SetValue(normalizedValue);
    }
}
