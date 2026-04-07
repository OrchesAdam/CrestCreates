using System;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.DistributedTransaction.CAP.Implementations;

public class DefaultCapTopicNameProvider : ICapTopicNameProvider
{
    public string GetTopicName<TEvent>() where TEvent : IDomainEvent
    {
        return GetTopicName(typeof(TEvent));
    }

    public string GetTopicName(IDomainEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return GetTopicName(@event.GetType());
    }

    protected virtual string GetTopicName(Type eventType)
    {
        var @namespace = eventType.Namespace;
        return string.IsNullOrWhiteSpace(@namespace)
            ? eventType.Name
            : $"{@namespace}.{eventType.Name}";
    }
}
