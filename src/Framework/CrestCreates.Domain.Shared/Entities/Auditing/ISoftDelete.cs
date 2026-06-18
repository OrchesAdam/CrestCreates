using System;

namespace CrestCreates.Domain.Shared.Entities.Auditing;

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletionTime { get; set; }
    Guid? DeleterId { get; set; }
}