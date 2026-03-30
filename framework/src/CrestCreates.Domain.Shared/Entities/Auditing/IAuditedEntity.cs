using System;

namespace CrestCreates.Domain.Shared.Entities.Auditing
{
    public interface IAuditedEntity
    {
        DateTime CreationTime { get; set; }
        Guid? CreatorId { get; set; }
        DateTime? LastModificationTime { get; set; }
        Guid? LastModifierId { get; set; }
    }

    public interface ISoftDelete
    {
        bool IsDeleted { get; set; }
        DateTime? DeletionTime { get; set; }
        Guid? DeleterId { get; set; }
    }

    public interface IFullyAuditedEntity : IAuditedEntity, ISoftDelete
    {
    }
}
