using System;

namespace CrestCreates.Domain.Shared.Entities.Auditing
{
    public interface IHasCreator
    {
        Guid? CreatorId { get; set; }
    }
}
