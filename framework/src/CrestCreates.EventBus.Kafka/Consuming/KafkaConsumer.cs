using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Serialization;

namespace CrestCreates.EventBus.Kafka.Consuming;

/// <summary>
/// Background service that consumes messages from Kafka topics and dispatches them to handlers.
/// Supports consumer groups for horizontal scaling, manual commits for exactly-once semantics,
/// and retry logic with dead letter queue fallback.
/// </summary>
public sealed class KafkaConsumer : BackgroundService
{
    private readonly KafkaProducerPool _producerPool;
    private readonly JsonSerializerContext _jsonContext;
    private readonly KafkaOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KafkaConsumer> _logger;
    private readonly List<KafkaSubscriptionInfo> _subscriptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaConsumer"/> class.
    /// </summary>
    /// <param name="producerPool">The producer pool for retry publishing.</param>
    /// <param name="jsonContext">The JSON serializer context for deserialization.</param>
    /// <param name="options">The Kafka options.</param>
    /// <param name="serviceProvider">The service provider for creating scopes.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
    public KafkaConsumer(
        KafkaProducerPool producerPool,
        JsonSerializerContext jsonContext,
        IOptions<KafkaOptions> options,
        IServiceProvider serviceProvider,
        ILogger<KafkaConsumer> logger)
    {
        _producerPool = producerPool ?? throw new ArgumentNullException(nameof(producerPool));
        _jsonContext = jsonContext ?? throw new ArgumentNullException(nameof(jsonContext));
        _options = options.Value;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _subscriptions = GetSubscriptions();
    }

    private static List<KafkaSubscriptionInfo> GetSubscriptions()
    {
        var registryType = Type.GetType("CrestCreates.EventBus.Kafka.Generated.KafkaSubscriptionRegistry, CrestCreates.EventBus.Kafka");
        if (registryType == null)
        {
            return new List<KafkaSubscriptionInfo>();
        }

        var method = registryType.GetMethod("GetSubscriptions", BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            return new List<KafkaSubscriptionInfo>();
        }

        var result = method.Invoke(null, null);
        return result as List<KafkaSubscriptionInfo> ?? new List<KafkaSubscriptionInfo>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptions.Count == 0)
        {
            _logger.LogWarning("No Kafka subscriptions found. Consumer will not start.");
            return;
        }

        _logger.LogInformation("Starting Kafka consumer with {Count} subscriptions", _subscriptions.Count);

        // Group subscriptions by consumer group (using handler type as proxy)
        var consumerGroups = _subscriptions
            .GroupBy(s => s.HandlerType.AssemblyQualifiedName ?? "default-group");

        var consumerTasks = new List<Task>();

        foreach (var group in consumerGroups)
        {
            var consumerTask = ConsumeWithConsumerGroupAsync(group.ToList(), stoppingToken);
            consumerTasks.Add(consumerTask);
        }

        await Task.WhenAll(consumerTasks);
    }

    private async Task ConsumeWithConsumerGroupAsync(
        List<KafkaSubscriptionInfo> subscriptions,
        CancellationToken stoppingToken)
    {
        var topics = subscriptions.Select(s => s.Topic).Distinct().ToList();
        var consumerGroup = subscriptions.First().HandlerType.AssemblyQualifiedName ?? _options.ConsumerGroupId;

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = consumerGroup,
            EnableAutoCommit = _options.EnableAutoCommit,
            AutoCommitIntervalMs = _options.AutoCommitIntervalMs,
            SessionTimeoutMs = _options.SessionTimeoutMs,
            MaxPollIntervalMs = _options.MaxPollIntervalMs,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = false
        };

        if (!string.IsNullOrEmpty(_options.SaslUsername) && !string.IsNullOrEmpty(_options.SaslPassword))
        {
            consumerConfig.SecurityProtocol = Enum.Parse<SecurityProtocol>(_options.SecurityProtocol);
            consumerConfig.SaslMechanism = Enum.Parse<SaslMechanism>(_options.SaslMechanism);
            consumerConfig.SaslUsername = _options.SaslUsername;
            consumerConfig.SaslPassword = _options.SaslPassword;
        }

        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig)
            .SetErrorHandler((c, e) =>
            {
                _logger.LogError("Kafka consumer error: {Reason} (code: {Code})", e.Reason, e.Code);
            })
            .SetPartitionsAssignedHandler((c, partitions) =>
            {
                _logger.LogInformation("Partitions assigned: {Partitions}", string.Join(", ", partitions));
            })
            .SetPartitionsRevokedHandler((c, partitions) =>
            {
                _logger.LogWarning("Partitions revoked: {Partitions}", string.Join(", ", partitions));
            })
            .Build();

        consumer.Subscribe(topics);

        _logger.LogInformation(
            "Kafka consumer subscribed to topics: {Topics} with group: {ConsumerGroup}",
            string.Join(", ", topics), consumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(stoppingToken);

                    if (consumeResult == null)
                        continue;

                    await ProcessMessageAsync(consumer, consumeResult, subscriptions, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error consuming message");
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> consumeResult,
        List<KafkaSubscriptionInfo> subscriptions,
        CancellationToken cancellationToken)
    {
        KafkaMessageEnvelope? envelope = null;

        try
        {
            envelope = JsonSerializer.Deserialize(
                consumeResult.Message.Value,
                KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);

            if (envelope == null)
            {
                _logger.LogError("Failed to deserialize message envelope");
                return;
            }

            var subscription = subscriptions.FirstOrDefault(s => s.Topic == consumeResult.Topic);
            if (subscription == null)
            {
                _logger.LogWarning("No subscription found for topic {Topic}", consumeResult.Topic);
                return;
            }

            using var scope = _serviceProvider.CreateScope();

            var eventTypeInfo = _jsonContext.GetTypeInfo(subscription.EventType);
            if (eventTypeInfo == null)
            {
                _logger.LogError("Event type {EventType} is not registered in JsonSerializerContext", subscription.EventType.Name);
                return;
            }

            var eventPayload = JsonSerializer.Deserialize(envelope.Payload, eventTypeInfo);

            if (eventPayload == null)
            {
                _logger.LogError("Failed to deserialize event payload for type {EventType}", envelope.EventType);
                return;
            }

            await subscription.InvokeHandler(scope.ServiceProvider, eventPayload, cancellationToken);

            // Manual commit
            consumer.Commit(consumeResult);

            _logger.LogInformation(
                "Successfully handled event {EventType} from topic {Topic} partition {Partition} offset {Offset}",
                envelope.EventType, consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from topic {Topic}", consumeResult.Topic);

            var retryCount = envelope?.RetryCount ?? 0;

            if (retryCount < _options.RetryCount)
            {
                // Increment retry count and republish
                var updatedEnvelope = UpdateRetryCount(consumeResult.Message.Value, retryCount + 1);

                var producer = await _producerPool.GetProducerAsync(cancellationToken);
                try
                {
                    await producer.ProduceAsync(
                        consumeResult.Topic,
                        new Message<string, byte[]>
                        {
                            Key = consumeResult.Message.Key,
                            Value = updatedEnvelope,
                            Headers = consumeResult.Message.Headers
                        },
                        cancellationToken);

                    // Commit the failed message to move past it
                    consumer.Commit(consumeResult);

                    _logger.LogWarning(
                        "Retrying message, attempt {Attempt} of {MaxRetries}",
                        retryCount + 1, _options.RetryCount);
                }
                finally
                {
                    _producerPool.ReturnProducer(producer);
                }
            }
            else
            {
                // Max retries reached, send to DLQ
                var dlqTopic = $"{consumeResult.Topic}{_options.DeadLetterTopicSuffix}";

                var producer = await _producerPool.GetProducerAsync(cancellationToken);
                try
                {
                    await producer.ProduceAsync(
                        dlqTopic,
                        new Message<string, byte[]>
                        {
                            Key = consumeResult.Message.Key,
                            Value = consumeResult.Message.Value,
                            Headers = consumeResult.Message.Headers ?? new Headers()
                        },
                        cancellationToken);

                    // Commit to move past the failed message
                    consumer.Commit(consumeResult);

                    _logger.LogError(
                        "Max retries ({MaxRetries}) reached, sent to DLQ topic {DlqTopic}",
                        _options.RetryCount, dlqTopic);
                }
                finally
                {
                    _producerPool.ReturnProducer(producer);
                }
            }
        }
    }

    private static byte[] UpdateRetryCount(byte[] originalMessage, int newRetryCount)
    {
        var envelope = JsonSerializer.Deserialize(
            originalMessage,
            KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);

        if (envelope == null)
            return originalMessage;

        envelope.RetryCount = newRetryCount;

        return JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
    }
}