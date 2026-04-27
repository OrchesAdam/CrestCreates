using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.RabbitMQ.Options;

/// <summary>
/// Delegate for invoking a handler method without reflection.
/// </summary>
/// <param name="serviceProvider">The service provider to resolve dependencies.</param>
/// <param name="eventPayload">The deserialized event payload.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>A task representing the async operation.</returns>
public delegate Task RabbitMqHandlerInvoker(
    IServiceProvider serviceProvider,
    object? eventPayload,
    CancellationToken cancellationToken);

/// <summary>
/// Contains information about a RabbitMQ subscription.
/// </summary>
/// <param name="EventType">The event type name (used as routing key).</param>
/// <param name="HandlerType">The handler type that processes the event.</param>
/// <param name="HandlerMethod">The method name on the handler that processes the event.</param>
/// <param name="Exchange">The exchange to subscribe to.</param>
/// <param name="Queue">The queue to consume from.</param>
/// <param name="PrefetchCount">The prefetch count for this subscription.</param>
/// <param name="InvokeHandler">A delegate that invokes the handler method without reflection.</param>
public sealed record RabbitMqSubscriptionInfo(
    string EventType,
    Type HandlerType,
    string HandlerMethod,
    string Exchange,
    string Queue,
    int PrefetchCount,
    RabbitMqHandlerInvoker InvokeHandler
);
