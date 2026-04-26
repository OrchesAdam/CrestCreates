using System;

namespace CrestCreates.EventBus.RabbitMQ.Options;

public sealed record RabbitMqSubscriptionInfo(
    string EventType,
    Type HandlerType,
    string HandlerMethod,
    string Exchange,
    string Queue,
    int PrefetchCount
);
