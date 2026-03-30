using System;

namespace CrestCreates.Domain.Entities
{
    public interface IEntity<TId> where TId : IEquatable<TId>
    {
        TId Id { get; }
    }
}
