using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.EventBus.Tests.Events;

public sealed class TestChannelLocalEvent : DomainEvent
{
    public TestChannelLocalEvent(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
