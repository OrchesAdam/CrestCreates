using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Background service that consumes messages from RabbitMQ queues.
/// Uses RabbitMQ.Client 7.x async API with IChannel.
/// </summary>
public sealed class RabbitMqConsumer : BackgroundService
{
    private readonly RabbitMqConnectionPool _connectionPool;
    private readonly RabbitMqOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly JsonSerializerContext? _jsonSerializerContext;
    private readonly IReadOnlyList<RabbitMqSubscriptionInfo> _subscriptions;

    private IChannel? _consumerChannel;
    private readonly List<string> _consumerTags = new();

    public RabbitMqConsumer(
        RabbitMqConnectionPool connectionPool,
        IOptions<RabbitMqOptions> options,
        IServiceProvider serviceProvider,
        ILogger<RabbitMqConsumer> logger,
        JsonSerializerContext? jsonSerializerContext = null)
    {
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _options = options.Value;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonSerializerContext = jsonSerializerContext;

        // Get subscriptions from generated registry
        _subscriptions = GetSubscriptions();
    }

    /// <summary>
    /// Gets subscriptions from the generated RabbitMqSubscriptionRegistry.
    /// </summary>
    private IReadOnlyList<RabbitMqSubscriptionInfo> GetSubscriptions()
    {
        try
        {
            // The source generator creates CrestCreates.EventBus.RabbitMQ.Generated.RabbitMqSubscriptionRegistry
            var registryType = Type.GetType("CrestCreates.EventBus.RabbitMQ.Generated.RabbitMqSubscriptionRegistry, CrestCreates.EventBus.RabbitMQ");
            if (registryType == null)
            {
                _logger.LogWarning("RabbitMqSubscriptionRegistry not found. No subscriptions will be registered.");
                return Array.Empty<RabbitMqSubscriptionInfo>();
            }

            var method = registryType.GetMethod("GetSubscriptions", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                _logger.LogWarning("GetSubscriptions method not found on RabbitMqSubscriptionRegistry.");
                return Array.Empty<RabbitMqSubscriptionInfo>();
            }

            var result = method.Invoke(null, null);
            return result as IReadOnlyList<RabbitMqSubscriptionInfo> ?? Array.Empty<RabbitMqSubscriptionInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions from RabbitMqSubscriptionRegistry.");
            return Array.Empty<RabbitMqSubscriptionInfo>();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptions.Count == 0)
        {
            _logger.LogInformation("No RabbitMQ subscriptions found. Consumer not starting.");
            return;
        }

        _logger.LogInformation("Starting RabbitMQ consumer with {Count} subscriptions", _subscriptions.Count);

        try
        {
            _consumerChannel = await _connectionPool.GetChannelAsync(stoppingToken);

            // Declare DLX exchange first
            await DeclareDeadLetterExchangeAsync(_consumerChannel, stoppingToken);

            // Setup each subscription
            foreach (var subscription in _subscriptions)
            {
                await SetupSubscriptionAsync(_consumerChannel, subscription, stoppingToken);
            }

            // Wait for cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RabbitMQ consumer shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in RabbitMQ consumer.");
            throw;
        }
    }

    private async Task DeclareDeadLetterExchangeAsync(IChannel channel, CancellationToken cancellationToken)
    {
        // Declare the dead letter exchange
        await channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Declared dead letter exchange: {Exchange}", _options.DeadLetterExchange);
    }

    private async Task SetupSubscriptionAsync(
        IChannel channel,
        RabbitMqSubscriptionInfo subscription,
        CancellationToken cancellationToken)
    {
        // Declare the exchange
        await channel.ExchangeDeclareAsync(
            exchange: subscription.Exchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // Declare the DLQ for this subscription
        var dlqName = $"{subscription.Queue}.dlq";
        await channel.QueueDeclareAsync(
            queue: dlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // Bind DLQ to DLX
        await channel.QueueBindAsync(
            queue: dlqName,
            exchange: _options.DeadLetterExchange,
            routingKey: subscription.Queue,
            arguments: null,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Declared and bound DLQ: {DlqName}", dlqName);

        // Declare the main queue with DLX arguments
        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = subscription.Queue
        };

        await channel.QueueDeclareAsync(
            queue: subscription.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken);

        // Bind queue to exchange
        await channel.QueueBindAsync(
            queue: subscription.Queue,
            exchange: subscription.Exchange,
            routingKey: subscription.EventType,
            arguments: null,
            cancellationToken: cancellationToken);

        _logger.LogDebug(
            "Declared queue {Queue} bound to exchange {Exchange} with routing key {RoutingKey}",
            subscription.Queue, subscription.Exchange, subscription.EventType);

        // Set prefetch count
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)subscription.PrefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        // Create and register consumer
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (sender, args) => HandleMessageAsync(channel, subscription, args, cancellationToken);

        var consumerTag = await channel.BasicConsumeAsync(
            queue: subscription.Queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        _consumerTags.Add(consumerTag);

        _logger.LogInformation(
            "Started consuming from queue {Queue} with consumer tag {ConsumerTag}",
            subscription.Queue, consumerTag);
    }

    private async Task HandleMessageAsync(
        IChannel channel,
        RabbitMqSubscriptionInfo subscription,
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        var correlationId = args.BasicProperties?.CorrelationId;
        var retryCount = GetRetryCount(args.BasicProperties);

        try
        {
            // Deserialize the envelope
            var envelope = DeserializeEnvelope(args.Body.ToArray());
            if (envelope == null)
            {
                _logger.LogWarning(
                    "Failed to deserialize message envelope for event type {EventType}, sending to DLQ",
                    subscription.EventType);

                // Reject without requeue (goes to DLQ)
                await channel.BasicNackAsync(
                    deliveryTag: args.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: cancellationToken);
                return;
            }

            _logger.LogDebug(
                "Processing message for event type {EventType} from queue {Queue}, retry count: {RetryCount}",
                envelope.EventType, subscription.Queue, retryCount);

            // Resolve handler from DI
            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService(subscription.HandlerType);

            // Get the handler method
            var method = subscription.HandlerType.GetMethod(
                subscription.HandlerMethod,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod);

            if (method == null)
            {
                _logger.LogError(
                    "Handler method {Method} not found on type {HandlerType}",
                    subscription.HandlerMethod, subscription.HandlerType.Name);

                await channel.BasicNackAsync(
                    deliveryTag: args.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: cancellationToken);
                return;
            }

            // Deserialize the payload
            var eventType = GetEventType(envelope.EventType);
            if (eventType == null)
            {
                _logger.LogWarning(
                    "Event type {EventType} not found, sending to DLQ",
                    envelope.EventType);

                await channel.BasicNackAsync(
                    deliveryTag: args.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: cancellationToken);
                return;
            }

            var payload = JsonSerializer.Deserialize(envelope.Payload, eventType, _jsonSerializerContext ?? RabbitMqMessageEnvelopeContext.Default);

            // Prepare parameters - detect method signature
            var parameters = method.GetParameters();
            object?[] invokeArgs;

            if (parameters.Length == 2 && parameters[1].ParameterType == typeof(CancellationToken))
            {
                invokeArgs = new object?[] { payload, cancellationToken };
            }
            else if (parameters.Length == 1)
            {
                invokeArgs = new object?[] { payload };
            }
            else
            {
                _logger.LogError(
                    "Handler method {Method} has unsupported signature on type {HandlerType}",
                    subscription.HandlerMethod, subscription.HandlerType.Name);

                await channel.BasicNackAsync(
                    deliveryTag: args.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: cancellationToken);
                return;
            }

            // Invoke the handler
            var result = method.Invoke(handler, invokeArgs);

            if (result is Task task)
            {
                await task;
            }

            // Acknowledge the message
            await channel.BasicAckAsync(
                deliveryTag: args.DeliveryTag,
                multiple: false,
                cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Successfully processed event {EventType} from queue {Queue}",
                envelope.EventType, subscription.Queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing message from queue {Queue}, retry count: {RetryCount}",
                subscription.Queue, retryCount);

            if (retryCount < _options.RetryCount)
            {
                // Retry with requeue
                _logger.LogInformation(
                    "Requeuing message for retry {RetryCount}/{MaxRetry} for queue {Queue}",
                    retryCount + 1, _options.RetryCount, subscription.Queue);

                await channel.BasicNackAsync(
                    deliveryTag: args.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken: cancellationToken);

                // Add delay before reprocessing (the message will be redelivered)
                await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), cancellationToken);
            }
            else
            {
                // Max retries exceeded, send to DLQ
                _logger.LogWarning(
                    "Max retries ({MaxRetry}) exceeded for queue {Queue}, sending to DLQ",
                    _options.RetryCount, subscription.Queue);

                await channel.BasicNackAsync(
                    deliveryTag: args.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: cancellationToken);

                throw new RabbitMqConsumeException(
                    $"Failed to process message from queue '{subscription.Queue}' after {_options.RetryCount} retries",
                    subscription.EventType,
                    correlationId,
                    retryCount,
                    ex);
            }
        }
    }

    private RabbitMqMessageEnvelope? DeserializeEnvelope(byte[] body)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(body);
            return JsonSerializer.Deserialize(json, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize message envelope");
            return null;
        }
    }

    private static int GetRetryCount(IReadOnlyBasicProperties? properties)
    {
        if (properties?.Headers == null)
            return 0;

        if (properties.Headers.TryGetValue("x-retry-count", out var value) && value is byte[] bytes)
        {
            var retryStr = System.Text.Encoding.UTF8.GetString(bytes);
            if (int.TryParse(retryStr, out var retryCount))
                return retryCount;
        }

        return 0;
    }

    private Type? GetEventType(string eventTypeName)
    {
        try
        {
            // Try to find the event type by name in loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(eventTypeName);
                if (type != null)
                    return type;

                // Try to find by short name
                foreach (var exportedType in assembly.GetExportedTypes())
                {
                    if (exportedType.Name == eventTypeName || exportedType.FullName == eventTypeName)
                        return exportedType;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve event type: {EventTypeName}", eventTypeName);
            return null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping RabbitMQ consumer...");

        if (_consumerChannel != null)
        {
            try
            {
                // Cancel all consumers
                foreach (var consumerTag in _consumerTags)
                {
                    await _consumerChannel.BasicCancelAsync(consumerTag, cancellationToken: cancellationToken);
                }

                _connectionPool.ReturnChannel(_consumerChannel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping consumer");
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
