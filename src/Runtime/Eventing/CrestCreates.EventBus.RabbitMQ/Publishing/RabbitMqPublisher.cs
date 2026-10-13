using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.EventBus.RabbitMQ.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Publishing;

/// <summary>
/// The single publish core: typed envelope creation for frozen event bindings and raw republish for the
/// retry path, sharing one confirm/property-copy engine. Publisher channels come from the bounded pool
/// with SDK publisher-confirm tracking enabled, so awaiting BasicPublishAsync participates in the
/// broker confirmation; no parallel homemade confirm ledger exists.
/// </summary>
public sealed class RabbitMqPublisher
{
    private readonly RabbitMqConnectionPool _connectionPool;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(
        RabbitMqConnectionPool connectionPool,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger)
    {
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Publishes one frozen binding as a new-format envelope (EventId/EventVersion written, EventType =
    /// canonical Name) and waits for the broker confirm.
    /// </summary>
    public async Task PublishEventAsync(
        RabbitMqPublishBinding binding,
        object payload,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(payload);

        var payloadJson = JsonSerializer.Serialize(payload, binding.Codec);
        var envelope = new RabbitMqMessageEnvelope(binding.Descriptor.Name, payloadJson)
        {
            EventId = binding.Descriptor.Id,
            EventVersion = binding.Descriptor.Version
        };

        if (!string.IsNullOrEmpty(correlationId))
        {
            envelope.Headers["CorrelationId"] = correlationId;
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
        var properties = BuildProperties(
            messageId: Guid.NewGuid().ToString(),
            correlationId: correlationId,
            eventName: binding.Descriptor.Name,
            timestamp: envelope.Timestamp,
            headers: null);

        await SendAsync(
            _options.DefaultExchange,
            binding.RoutingKey,
            body,
            properties,
            declareExchange: true,
            eventName: binding.Descriptor.Name,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Published event {EventName} v{Version} to exchange {Exchange} with routing key {RoutingKey}",
            binding.Descriptor.Name, binding.Descriptor.Version, _options.DefaultExchange, binding.RoutingKey);
    }

    /// <summary>
    /// Business-retry redelivery: republishes the original body to the original queue through the
    /// default exchange with the cloned properties (identity preserved; only the framework-owned retry
    /// header changed) and waits for the confirm. The caller acks the original delivery only after
    /// this method completes successfully.
    /// </summary>
    public async Task RepublishToQueueAsync(
        string queue,
        ReadOnlyMemory<byte> body,
        BasicProperties properties,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentNullException.ThrowIfNull(properties);

        await SendAsync(
            exchange: string.Empty,
            routingKey: queue,
            body,
            properties,
            declareExchange: false,
            eventName: properties.Type ?? queue,
            properties.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Redelivered message {MessageId} to queue {Queue} for retry",
            properties.MessageId, queue);
    }

    /// <summary>
    /// Clones the original AMQP properties for a redelivery: identity and app headers are preserved,
    /// only the framework-owned retry fields are modified by the caller afterwards.
    /// </summary>
    public static BasicProperties CloneProperties(IReadOnlyBasicProperties? source, Dictionary<string, object?>? replacementHeaders = null)
    {
        var clone = new BasicProperties
        {
            AppId = source?.AppId,
            ClusterId = source?.ClusterId,
            ContentEncoding = source?.ContentEncoding,
            ContentType = source?.ContentType,
            CorrelationId = source?.CorrelationId,
            DeliveryMode = source?.DeliveryMode ?? DeliveryModes.Persistent,
            Expiration = source?.Expiration,
            MessageId = source?.MessageId,
            Persistent = source?.Persistent ?? true,
            Priority = source?.Priority ?? 0,
            ReplyTo = source?.ReplyTo,
            Timestamp = source?.Timestamp ?? default,
            Type = source?.Type,
            UserId = source?.UserId,
            Headers = replacementHeaders ?? CloneHeaders(source?.Headers)
        };

        return clone;
    }

    private static Dictionary<string, object?> CloneHeaders(IDictionary<string, object?>? source)
    {
        var clone = new Dictionary<string, object?>();
        if (source is null)
        {
            return clone;
        }

        foreach (var pair in source)
        {
            clone[pair.Key] = pair.Value;
        }

        return clone;
    }

    private static BasicProperties BuildProperties(
        string messageId,
        string? correlationId,
        string eventName,
        DateTime timestamp,
        Dictionary<string, object?>? headers)
        => new()
        {
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = messageId,
            CorrelationId = correlationId,
            Type = eventName,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)).ToUnixTimeSeconds()),
            ContentType = "application/json",
            Headers = headers
        };

    private async Task SendAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        BasicProperties properties,
        bool declareExchange,
        string eventName,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        IChannel? channel = null;
        try
        {
            channel = await _connectionPool.GetChannelAsync(cancellationToken).ConfigureAwait(false);

            if (declareExchange)
            {
                await channel.ExchangeDeclareAsync(
                    exchange: exchange,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            using var confirmTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            confirmTimeout.CancelAfter(TimeSpan.FromSeconds(_options.PublisherConfirmTimeoutSeconds));

            // With publisher-confirmation tracking enabled, this await completes when the broker
            // confirms the publish (or faults on return/nack).
            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: confirmTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Publish cancelled by the caller for event {EventName}", eventName);
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new RabbitMqPublishException(
                $"Publish confirmation timed out after {_options.PublisherConfirmTimeoutSeconds}s for event '{eventName}'.",
                eventName,
                correlationId,
                ex,
                RabbitMqPublishFailureKind.Timeout);
        }
        catch (PublishReturnException ex)
        {
            throw new RabbitMqPublishException(
                $"Message returned for event '{eventName}': the broker could not route the mandatory publish " +
                $"({ex.Message}).",
                eventName,
                correlationId,
                ex,
                RabbitMqPublishFailureKind.Unroutable);
        }
        catch (PublishException ex)
        {
            throw new RabbitMqPublishException(
                $"Publisher confirmation nacked by the broker for event '{eventName}' ({ex.Message}).",
                eventName,
                correlationId,
                ex,
                RabbitMqPublishFailureKind.Nacked);
        }
        catch (Exception ex) when (ex is AlreadyClosedException or BrokerUnreachableException or System.IO.IOException)
        {
            throw new RabbitMqPublishException(
                $"Publish interrupted for event '{eventName}'; the outcome is indeterminate.",
                eventName,
                correlationId,
                ex,
                RabbitMqPublishFailureKind.Interrupted);
        }
        catch (RabbitMqPublishException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RabbitMqPublishException(
                $"Failed to publish event '{eventName}'.",
                eventName,
                correlationId,
                ex,
                RabbitMqPublishFailureKind.Failed);
        }
        finally
        {
            if (channel is not null)
            {
                // Exactly-once lease release: ReturnChannel handles both open (pooled) and closed
                // (discarded) channels and releases the semaphore permit on every path.
                _connectionPool.ReturnChannel(channel);
            }
        }
    }
}
