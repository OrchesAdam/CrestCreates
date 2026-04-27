using System;
using System.Collections.Generic;
using System.Linq;
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
using CrestCreates.EventBus.Kafka.Generated;
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
    private IConsumer<string, byte[]>? _consumer;
    private readonly CancellationTokenSource _stopCts = new();

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

        _subscriptions = KafkaSubscriptionRegistry.GetSubscriptions().ToList();
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

        var topics = _subscriptions.Select(s => s.Topic).Distinct().ToList();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            EnableAutoCommit = _options.EnableAutoCommit,
            AutoCommitIntervalMs = _options.AutoCommitIntervalMs,
            SessionTimeoutMs = _options.SessionTimeoutMs,
            MaxPollIntervalMs = _options.MaxPollIntervalMs,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = false
        };

        if (!string.IsNullOrEmpty(_options.SaslUsername) && !string.IsNullOrEmpty(_options.SaslPassword))
        {
            if (!Enum.TryParse<SecurityProtocol>(_options.SecurityProtocol, out var securityProtocol))
            {
                throw new ArgumentException(
                    $"Invalid SecurityProtocol value: '{_options.SecurityProtocol}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<SecurityProtocol>())}");
            }

            if (!Enum.TryParse<SaslMechanism>(_options.SaslMechanism, out var saslMechanism))
            {
                throw new ArgumentException(
                    $"Invalid SaslMechanism value: '{_options.SaslMechanism}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<SaslMechanism>())}");
            }

            consumerConfig.SecurityProtocol = securityProtocol;
            consumerConfig.SaslMechanism = saslMechanism;
            consumerConfig.SaslUsername = _options.SaslUsername;
            consumerConfig.SaslPassword = _options.SaslPassword;
        }

        _consumer = new ConsumerBuilder<string, byte[]>(consumerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason} (code: {Code})", e.Reason, e.Code))
            .SetPartitionsAssignedHandler((_, partitions) => _logger.LogInformation("Partitions assigned: {Partitions}", string.Join(", ", partitions)))
            .SetPartitionsRevokedHandler((_, partitions) => _logger.LogWarning("Partitions revoked: {Partitions}", string.Join(", ", partitions)))
            .Build();

        _consumer.Subscribe(topics);

        _logger.LogInformation(
            "Kafka consumer subscribed to topics: {Topics} with group: {ConsumerGroup}",
            string.Join(", ", topics), _options.ConsumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult == null)
                        continue;

                    await ProcessMessageAsync(consumeResult, stoppingToken);
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
            _consumer.Close();
            _consumer.Dispose();
            _consumer = null;
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Kafka consumer");
        _stopCts.Cancel();

        try
        {
            _consumer?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing consumer during stop");
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(
        ConsumeResult<string, byte[]> consumeResult,
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

            var subscription = _subscriptions.FirstOrDefault(s => s.Topic == consumeResult.Topic);
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
            _consumer!.Commit(consumeResult);

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
                // Add delay before retry
                if (_options.RetryDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), cancellationToken);
                }

                // Check if cancellation was requested during the delay
                cancellationToken.ThrowIfCancellationRequested();

                // Increment retry count and republish
                var updatedEnvelope = UpdateRetryCount(consumeResult.Message.Value, retryCount + 1);

                // Preserve headers and add retry count header for traceability
                var retryHeaders = consumeResult.Message.Headers ?? new Headers();
                retryHeaders.Add("x-retry-count", System.Text.Encoding.UTF8.GetBytes((retryCount + 1).ToString()));

                var producer = await _producerPool.GetProducerAsync(cancellationToken);
                try
                {
                    await producer.ProduceAsync(
                        consumeResult.Topic,
                        new Message<string, byte[]>
                        {
                            Key = consumeResult.Message.Key,
                            Value = updatedEnvelope,
                            Headers = retryHeaders
                        },
                        cancellationToken);

                    // Commit the failed message to move past it
                    _consumer!.Commit(consumeResult);

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

                // Add retry count header for traceability
                var dlqHeaders = consumeResult.Message.Headers ?? new Headers();
                dlqHeaders.Add("x-retry-count", System.Text.Encoding.UTF8.GetBytes(retryCount.ToString()));
                dlqHeaders.Add("x-original-topic", System.Text.Encoding.UTF8.GetBytes(consumeResult.Topic));

                var producer = await _producerPool.GetProducerAsync(cancellationToken);
                try
                {
                    await producer.ProduceAsync(
                        dlqTopic,
                        new Message<string, byte[]>
                        {
                            Key = consumeResult.Message.Key,
                            Value = consumeResult.Message.Value,
                            Headers = dlqHeaders
                        },
                        cancellationToken);

                    // Commit to move past the failed message
                    _consumer!.Commit(consumeResult);

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