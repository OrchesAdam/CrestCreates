using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.DistributedTransaction.CAP.Implementations;

public class CapEventBus : DistributedEventBusBase
{
    private readonly IDistributedEventPublisher _eventPublisher;
    private readonly ICapTopicNameProvider _topicNameProvider;

    public CapEventBus(
        IDistributedEventPublisher eventPublisher,
        ICapTopicNameProvider topicNameProvider)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _topicNameProvider = topicNameProvider ?? throw new ArgumentNullException(nameof(topicNameProvider));
    }

    public override Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var topicName = _topicNameProvider.GetTopicName(@event);
        return _eventPublisher.PublishAsync(topicName, @event, cancellationToken: cancellationToken);
    }

    public override Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var topicName = _topicNameProvider.GetTopicName<TEvent>();
        return _eventPublisher.PublishAsync(topicName, @event, cancellationToken: cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException("CAP 使用 [CapSubscribe] 发现订阅者，请在订阅方法上声明 CapSubscribeAttribute。");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException("CAP 订阅由运行时发现管理，不支持通过 IEventBus 动态取消订阅。");
    }
}
