using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Registration;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Consumer-side topology: dead-letter exchange, per-plan queue/DLQ declarations, bindings, QoS and
/// consumer registration. All declarations share the consumer's settlement gate so topology changes
/// never interleave with an in-flight settlement.
/// </summary>
internal sealed class RabbitMqConsumerTopology
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _settlementGate;
    private readonly ILogger<RabbitMqConsumer> _logger;

    public RabbitMqConsumerTopology(
        RabbitMqOptions options,
        SemaphoreSlim settlementGate,
        ILogger<RabbitMqConsumer> logger)
    {
        _options = options;
        _settlementGate = settlementGate;
        _logger = logger;
    }

    public async Task DeclareDeadLetterExchangeAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await _settlementGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.ExchangeDeclareAsync(
                exchange: _options.DeadLetterExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _settlementGate.Release();
        }

        _logger.LogDebug("Declared dead letter exchange: {Exchange}", _options.DeadLetterExchange);
    }

    public async Task<string> SetupQueueAsync(
        IChannel channel,
        RabbitMqQueuePlan plan,
        Func<RabbitMqQueuePlan, BasicDeliverEventArgs, Task> onReceived,
        CancellationToken cancellationToken)
    {
        await _settlementGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var exchanges = plan.Declarations.Select(d => d.Exchange).Distinct(StringComparer.Ordinal).ToList();
            foreach (var exchange in exchanges)
            {
                await channel.ExchangeDeclareAsync(
                    exchange: exchange,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            var deadLetterQueue = $"{plan.Queue}.dlq";
            await channel.QueueDeclareAsync(
                queue: deadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await channel.QueueBindAsync(
                queue: deadLetterQueue,
                exchange: _options.DeadLetterExchange,
                routingKey: plan.Queue,
                arguments: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var queueArguments = new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _options.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = plan.Queue
            };

            await channel.QueueDeclareAsync(
                queue: plan.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var declaration in plan.Declarations)
            {
                await channel.QueueBindAsync(
                    queue: plan.Queue,
                    exchange: declaration.Exchange,
                    routingKey: declaration.RoutingKey,
                    arguments: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // Per-consumer QoS (global: false) applies the queue plan's prefetch to the consumer
            // created below; default dispatch concurrency is configured explicitly on the factory.
            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: (ushort)plan.PrefetchCount,
                global: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => onReceived(plan, args);

            var consumerTag = await channel.BasicConsumeAsync(
                queue: plan.Queue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Consuming from queue {Queue} (prefetch {Prefetch}, {Identities} declared identity/identities, consumer tag {ConsumerTag})",
                plan.Queue, plan.PrefetchCount, plan.HandlersByIdentity.Count, consumerTag);

            return consumerTag;
        }
        finally
        {
            _settlementGate.Release();
        }
    }
}
