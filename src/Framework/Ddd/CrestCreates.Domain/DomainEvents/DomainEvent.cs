using System;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.Domain.DomainEvents
{
    public interface IDomainEvent : ILocalEvent
    {
        DateTime OccurredOn { get; }
    }

    public abstract class DomainEvent : IDomainEvent
    {
        public DateTime OccurredOn { get; }

        protected DomainEvent()
        {
            OccurredOn = DateTime.UtcNow;
        }
    }
}
