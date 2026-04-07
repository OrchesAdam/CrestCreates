using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.DistributedTransaction.CAP.Abstractions;

public interface ICapTopicNameProvider
{
    string GetTopicName<TEvent>() where TEvent : IDomainEvent;

    string GetTopicName(IDomainEvent @event);
}
