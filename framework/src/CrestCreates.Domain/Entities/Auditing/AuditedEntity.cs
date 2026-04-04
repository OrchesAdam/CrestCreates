using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing
{
    public abstract class AuditedEntity<TId> : Entity<TId>, IAuditedEntity<TId> where TId : IEquatable<TId>
    {
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
    }
}
