using System;
using CrestCreates.Domain.Entities.Auditing;

namespace CrestCreates.Domain.Entities;

public abstract class MustHaveTenantOrganizationEntity<TKey> : Entity<TKey>, IMustHaveTenantOrganization<TKey>
    where TKey : IEquatable<TKey>
{
    public string TenantId { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
}
