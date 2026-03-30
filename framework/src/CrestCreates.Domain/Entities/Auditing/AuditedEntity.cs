using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing
{
    public abstract class AuditedEntity<TId> : Entity<TId>, IAuditedEntity where TId : IEquatable<TId>
    {
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
    }

    public abstract class AuditedAggregateRoot<TId> : AggregateRoot<TId>, IAuditedEntity where TId : IEquatable<TId>
    {
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
    }

    public abstract class FullyAuditedEntity<TId> : AuditedEntity<TId>, IFullyAuditedEntity where TId : IEquatable<TId>
    {
        public bool IsDeleted { get; set; }
        public DateTime? DeletionTime { get; set; }
        public Guid? DeleterId { get; set; }
    }

    public abstract class FullyAuditedAggregateRoot<TId> : AuditedAggregateRoot<TId>, IFullyAuditedEntity where TId : IEquatable<TId>
    {
        public bool IsDeleted { get; set; }
        public DateTime? DeletionTime { get; set; }
        public Guid? DeleterId { get; set; }
    }
}
