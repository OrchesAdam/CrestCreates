using CrestCreates.Domain.Entities;
using System;

namespace CrestCreates.DataFilter.Entities;

public abstract class MultiTenantEntity<TId> : Entity<TId>, IMultiTenant where TId : IEquatable<TId>
{
    public string? TenantId { get; set; }
}