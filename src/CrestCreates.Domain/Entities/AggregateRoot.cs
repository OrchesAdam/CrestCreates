using System;

namespace CrestCreates.Domain.Entities
{
    public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : IEquatable<TId>
    {
        protected AggregateRoot()
        {
        }

        protected AggregateRoot(TId id)
        {
            Id = id;
        }
    }

    public interface IAggregateRoot
    {
    }
}
