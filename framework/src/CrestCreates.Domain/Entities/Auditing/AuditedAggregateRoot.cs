using System;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing;

public abstract class AuditedAggregateRoot<TId> : AggregateRoot<TId>, IAuditedEntity<TId> where TId : IEquatable<TId>
{
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
}