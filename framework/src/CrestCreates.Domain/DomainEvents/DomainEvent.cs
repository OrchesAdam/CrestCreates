using System;
using MediatR;

namespace CrestCreates.Domain.DomainEvents
{
    public interface IDomainEvent : INotification
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
