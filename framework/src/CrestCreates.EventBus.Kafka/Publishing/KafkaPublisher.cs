using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Exceptions;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Serialization;

namespace CrestCreates.EventBus.Kafka.Publishing;

/// <summary>
/// Provides functionality for publishing events to Kafka topics.
/// </summary>
public class KafkaPublisher
{
    private readonly KafkaProducerPool _producerPool;
    private readonly JsonSerializerContext _jsonContext;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaPublisher"/> class.
    /// </summary>
    /// <param name="producerPool">The Kafka producer pool.</param>
    /// <param name="jsonContext">The JSON serializer context for serialization.</param>
    /// <param name="options">The Kafka options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
    public KafkaPublisher(
        KafkaProducerPool producerPool,
        JsonSerializerContext jsonContext,
        IOptions<KafkaOptions> options,
        ILogger<KafkaPublisher> logger)
    {
        _producerPool = producerPool ?? throw new ArgumentNullException(nameof(producerPool));
        _jsonContext = jsonContext ?? throw new ArgumentNullException(nameof(jsonContext));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Publishes an event to the specified Kafka topic.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to publish.</typeparam>
    /// <param name="topic">The topic to publish to.</param>
    /// <param name="event">The event to publish.</param>
    /// <param name="key">Optional message key. If not provided, a GUID will be used.</param>
    /// <param name="headers">Optional headers to include in the message.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when event is null.</exception>
    /// <exception cref="ArgumentException">Thrown when topic is null or whitespace.</exception>
    /// <exception cref="KafkaPublishException">Thrown when publishing fails.</exception>
    public async Task PublishAsync<TEvent>(
        string topic,
        TEvent @event,
        string? key = null,
        Dictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        IProducer<string, byte[]>? producer = null;
        try
        {
            producer = await _producerPool.GetProducerAsync(cancellationToken);

            // Serialize event
            var eventType = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name;
            var typeInfo = _jsonContext.GetTypeInfo(typeof(TEvent));
            if (typeInfo == null)
            {
                throw new InvalidOperationException($"Type {typeof(TEvent).Name} is not registered in JsonSerializerContext.");
            }
            var payload = JsonSerializer.Serialize(@event, typeInfo);

            var envelope = new KafkaMessageEnvelope(eventType, payload ?? string.Empty, headers);

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);

            // Create message with headers
            var message = new Message<string, byte[]>
            {
                Key = key ?? Guid.NewGuid().ToString(),
                Value = messageBody,
                Timestamp = new Timestamp(DateTimeOffset.UtcNow)
            };

            if (headers != null)
            {
                message.Headers = new Headers();
                foreach (var header in headers)
                {
                    message.Headers.Add(header.Key, System.Text.Encoding.UTF8.GetBytes(header.Value ?? string.Empty));
                }
            }

            // Add event type header for routing
            message.Headers ??= new Headers();
            message.Headers.Add("event-type", System.Text.Encoding.UTF8.GetBytes(eventType));

            // Publish
            var deliveryResult = await producer.ProduceAsync(topic, message, cancellationToken);

            _logger.LogDebug(
                "Published event {EventType} to topic {Topic} partition {Partition} offset {Offset}",
                eventType, topic, deliveryResult.Partition, deliveryResult.Offset);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            _logger.LogError(ex, "Failed to publish event to topic {Topic}", topic);

            throw new KafkaPublishException(
                $"Failed to publish event: {ex.Message}",
                topic,
                ex.DeliveryResult?.Partition.Value,
                ex.DeliveryResult?.Offset.Value,
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to {Topic}", topic);

            throw new KafkaPublishException(
                $"Failed to publish event: {ex.Message}",
                topic,
                innerException: ex);
        }
        finally
        {
            if (producer != null)
            {
                _producerPool.ReturnProducer(producer);
            }
        }
    }

    /// <summary>
    /// Publishes a failed message to the dead letter topic for the specified original topic.
    /// </summary>
    /// <param name="originalTopic">The original topic from which the message failed.</param>
    /// <param name="messageBody">The raw message body to publish.</param>
    /// <param name="key">The message key.</param>
    /// <param name="retryCount">The number of retry attempts made.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="KafkaPublishException">Thrown when publishing to the dead letter topic fails.</exception>
    public async Task PublishToDeadLetterTopicAsync(
        string originalTopic,
        byte[] messageBody,
        string? key,
        int retryCount,
        CancellationToken cancellationToken = default)
    {
        var dlqTopic = $"{originalTopic}{_options.DeadLetterTopicSuffix}";

        IProducer<string, byte[]>? producer = null;
        try
        {
            producer = await _producerPool.GetProducerAsync(cancellationToken);

            var message = new Message<string, byte[]>
            {
                Key = key ?? Guid.NewGuid().ToString(),
                Value = messageBody,
                Timestamp = new Timestamp(DateTimeOffset.UtcNow),
                Headers = new Headers()
            };

            message.Headers.Add("original-topic", System.Text.Encoding.UTF8.GetBytes(originalTopic));
            message.Headers.Add("retry-count", System.Text.Encoding.UTF8.GetBytes(retryCount.ToString()));

            var deliveryResult = await producer.ProduceAsync(dlqTopic, message, cancellationToken);

            _logger.LogWarning(
                "Published message to dead letter topic {DlqTopic} from original topic {OriginalTopic}",
                dlqTopic, originalTopic);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            _logger.LogError(ex, "Failed to publish to dead letter topic {DlqTopic}", dlqTopic);
            throw new KafkaPublishException(
                $"Failed to publish to DLQ: {ex.Message}",
                dlqTopic,
                ex.DeliveryResult?.Partition.Value,
                ex.DeliveryResult?.Offset.Value,
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish to dead letter topic {DlqTopic}", dlqTopic);
            throw new KafkaPublishException(
                $"Failed to publish to DLQ: {ex.Message}",
                dlqTopic,
                innerException: ex);
        }
        finally
        {
            if (producer != null)
            {
                _producerPool.ReturnProducer(producer);
            }
        }
    }
}