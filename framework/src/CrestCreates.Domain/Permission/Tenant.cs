using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Permission;

public class Tenant : AuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public TenantLifecycleState LifecycleState { get; set; } = TenantLifecycleState.Active;
    public DateTime? ArchivedTime { get; set; }
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

    public void SetLifecycleState(TenantLifecycleState state)
    {
        if (state == TenantLifecycleState.Archived && LifecycleState != TenantLifecycleState.Archived)
        {
            ArchivedTime = DateTime.UtcNow;
        }
        LifecycleState = state;
    }

    public void Archive()
    {
        SetLifecycleState(TenantLifecycleState.Archived);
    }

    public void Restore()
    {
        SetLifecycleState(TenantLifecycleState.Active);
        ArchivedTime = null;
        IsActive = true;
    }

    public void SoftDelete()
    {
        SetLifecycleState(TenantLifecycleState.Deleted);
        IsActive = false;
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

    public void AddConnectionString(string name, string value)
    {
        var existingConnectionString = ConnectionStrings.FirstOrDefault(cs => cs.Name == name);
        if (existingConnectionString is not null)
        {
            existingConnectionString.SetValue(value);
            return;
        }

        ConnectionStrings.Add(new TenantConnectionString(Guid.NewGuid(), Id, name, value));
    }

    public bool RemoveConnectionString(string name)
    {
        if (name == TenantConnectionString.DefaultName)
        {
            throw new InvalidOperationException("默认连接串不能被删除");
        }

        var existingConnectionString = ConnectionStrings.FirstOrDefault(cs => cs.Name == name);
        if (existingConnectionString == null)
        {
            return false;
        }

        return ConnectionStrings.Remove(existingConnectionString);
    }

    public string? GetConnectionString(string name)
    {
        return ConnectionStrings.FirstOrDefault(cs => cs.Name == name)?.Value;
    }
}
