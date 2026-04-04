using System;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing;

public abstract class FullyAuditedEntity<TId> : AuditedEntity<TId>, IFullyAuditedEntity<TId> where TId : IEquatable<TId>
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletionTime { get; set; }
    public Guid? DeleterId { get; set; }
}