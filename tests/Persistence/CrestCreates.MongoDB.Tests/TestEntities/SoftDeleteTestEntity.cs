using System;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.MongoDB.Tests.TestEntities;

public class SoftDeleteTestEntity : IEntity<string>, ISoftDelete, IMustHaveTenant, IAuditedEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletionTime { get; set; }
    public Guid? DeleterId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    public Guid? CreatorId { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}
