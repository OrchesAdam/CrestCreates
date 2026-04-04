using System;

namespace CrestCreates.Domain.Shared.Entities
{
    public interface IEntity<TId> where TId : IEquatable<TId>
    {
        TId Id { get; }
    }
}
