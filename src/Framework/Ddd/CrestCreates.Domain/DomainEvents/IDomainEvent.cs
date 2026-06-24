using System;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.Domain.DomainEvents
{
    public interface IDomainEvent : ILocalEvent
    {
        DateTime OccurredOn { get; }
    }
}
