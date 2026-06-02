using System;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.MongoDB.Tests.TestEntities;

public class TenantTestEntity : IEntity<string>, IMustHaveTenant, IAuditedEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    public Guid? CreatorId { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}
