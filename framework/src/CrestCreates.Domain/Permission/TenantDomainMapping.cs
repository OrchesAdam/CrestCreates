using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class TenantDomainMapping : Entity<Guid>
{
    public Guid TenantId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string? Subdomain { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }

    public TenantDomainMapping()
    {
    }

    public TenantDomainMapping(Guid id, Guid tenantId, string domain)
    {
        Id = id;
        TenantId = tenantId;
        Domain = domain.ToLowerInvariant();
        CreationTime = DateTime.UtcNow;
    }

    public void SetSubdomain(string? subdomain)
    {
        Subdomain = subdomain?.ToLowerInvariant();
    }

    public void Activate()
    {
        IsActive = true;
        LastModificationTime = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        LastModificationTime = DateTime.UtcNow;
    }
}
