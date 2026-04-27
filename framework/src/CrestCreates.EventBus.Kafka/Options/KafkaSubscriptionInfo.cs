using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Kafka.Options;

/// <summary>
/// Contains information about a Kafka subscription.
/// </summary>
/// <param name="Topic">The topic to subscribe to.</param>
/// <param name="EventType">The event type name.</param>
/// <param name="HandlerType">The handler type that processes the event.</param>
/// <param name="HandlerMethod">The method name on the handler that processes the event.</param>
/// <param name="InvokeHandler">A delegate that invokes the handler method without reflection.</param>
public sealed record KafkaSubscriptionInfo(
    string Topic,
    Type EventType,
    Type HandlerType,
    string HandlerMethod,
    Func<IServiceProvider, object, CancellationToken, Task> InvokeHandler
);
