using System;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.EventBus.Tests.Events;

public sealed class TestLocalEvent : DomainEvent
{
    public TestLocalEvent(Guid entityId)
    {
        EntityId = entityId;
    }

    public Guid EntityId { get; }
}
