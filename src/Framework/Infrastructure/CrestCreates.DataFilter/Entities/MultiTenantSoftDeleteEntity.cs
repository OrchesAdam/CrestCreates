using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using System;

namespace CrestCreates.DataFilter.Entities;

public abstract class MultiTenantSoftDeleteEntity<TId> : Entity<TId>, ISoftDelete, IMultiTenant where TId : IEquatable<TId>
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletionTime { get; set; }
    public Guid? DeleterId { get; set; }
    public string? TenantId { get; set; }
}