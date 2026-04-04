using System;

namespace CrestCreates.Domain.Entities.Auditing
{
    public interface IHasCreator
    {
        Guid? CreatorId { get; set; }
    }
}
