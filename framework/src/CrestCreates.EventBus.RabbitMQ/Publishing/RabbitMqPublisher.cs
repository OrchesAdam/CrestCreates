using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Serialization;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.RabbitMQ.Publishing;

/// <summary>
/// Publisher for RabbitMQ events with publisher confirmation support.
/// Uses RabbitMQ.Client 7.x async API with event-based confirmations.
/// </summary>
public sealed class RabbitMqPublisher : IAsyncDisposable
{
    private readonly RabbitMqConnectionPool _connectionPool;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly JsonSerializerContext? _jsonSerializerContext;
    private bool _disposed;

    public RabbitMqPublisher(
        RabbitMqConnectionPool connectionPool,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger,
        JsonSerializerContext? jsonSerializerContext = null)
    {
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonSerializerContext = jsonSerializerContext;
    }

    /// <summary>
    /// Publishes an event to RabbitMQ with publisher confirmation.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="exchange">Optional exchange name. Uses default if not specified.</param>
    /// <param name="routingKey">Optional routing key. Uses event type name if not specified.</param>
    /// <param name="correlationId">Optional correlation ID for tracing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="RabbitMqPublishException">Thrown when publish fails.</exception>
    public async Task PublishAsync<TEvent>(
        TEvent @event,
        string? exchange = null,
        string? routingKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (@event == null)
        {
            throw new ArgumentNullException(nameof(@event));
        }

        var eventType = typeof(TEvent).Name;
        var actualExchange = exchange ?? _options.DefaultExchange;
        var actualRoutingKey = routingKey ?? eventType;

        IChannel? channel = null;
        var confirmationTracker = new PublishConfirmationTracker();
        var publishSucceeded = false;

        try
        {
            // Get a channel from the pool
            channel = await _connectionPool.GetChannelAsync(cancellationToken);

            // Setup confirmation event handlers for this publish operation
            channel.BasicAcksAsync += confirmationTracker.OnAckAsync;
            channel.BasicNacksAsync += confirmationTracker.OnNackAsync;
            channel.BasicReturnAsync += confirmationTracker.OnReturnAsync;

            // Declare exchange (idempotent operation)
            await channel.ExchangeDeclareAsync(
                exchange: actualExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Serialize the event to envelope
            var envelope = CreateEnvelope(@event, eventType, correlationId);
            var body = SerializeEnvelope(envelope);

            // Create basic properties with persistent delivery and mandatory flag
            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = Guid.NewGuid().ToString(),
                CorrelationId = correlationId,
                Type = eventType,
                Timestamp = new AmqpTimestamp(envelope.Timestamp.Ticks),
                ContentType = "application/json"
            };

            // Get the publish sequence number before publishing
            var sequenceNumber = await channel.GetNextPublishSequenceNumberAsync(cancellationToken);
            confirmationTracker.ExpectSequence(sequenceNumber);
            confirmationTracker.RegisterMessageId(properties.MessageId, sequenceNumber);

            // Publish the message
            await channel.BasicPublishAsync(
                exchange: actualExchange,
                routingKey: actualRoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            // Wait for publisher confirmation
            var confirmTimeout = TimeSpan.FromSeconds(_options.PublisherConfirmTimeoutSeconds);
            var result = await confirmationTracker.WaitForConfirmationAsync(sequenceNumber, confirmTimeout, cancellationToken);

            if (!result.Confirmed)
            {
                var message = result.Returned
                    ? $"Message returned for event type '{eventType}' - broker could not route message"
                    : $"Publisher confirmation failed (nacked) for event type '{eventType}'";

                throw new RabbitMqPublishException(message, eventType, correlationId);
            }

            publishSucceeded = true;

            _logger.LogDebug(
                "Published event {EventType} to exchange {Exchange} with routing key {RoutingKey}",
                eventType, actualExchange, actualRoutingKey);
        }
        catch (RabbitMqPublishException)
        {
            // Re-throw our own exceptions
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Publish cancelled for event type {EventType}", eventType);
            throw new RabbitMqPublishException(
                $"Publish operation cancelled for event type '{eventType}'",
                eventType,
                correlationId,
                ex);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Publish confirmation timeout for event type {EventType}", eventType);
            throw new RabbitMqPublishException(
                $"Publish confirmation timeout for event type '{eventType}'",
                eventType,
                correlationId,
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType}", eventType);
            throw new RabbitMqPublishException(
                $"Failed to publish event of type '{eventType}'",
                eventType,
                correlationId,
                ex);
        }
        finally
        {
            // Remove event handlers before returning/disposing channel
            if (channel != null)
            {
                channel.BasicAcksAsync -= confirmationTracker.OnAckAsync;
                channel.BasicNacksAsync -= confirmationTracker.OnNackAsync;
                channel.BasicReturnAsync -= confirmationTracker.OnReturnAsync;

                if (publishSucceeded)
                {
                    _connectionPool.ReturnChannel(channel);
                }
                else
                {
                    // Channel is disposed on error (not returned to pool)
                    channel.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Publishes an event to RabbitMQ with custom headers.
    /// </summary>
    public async Task PublishWithHeadersAsync<TEvent>(
        TEvent @event,
        Dictionary<string, string?> headers,
        string? exchange = null,
        string? routingKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (@event == null)
        {
            throw new ArgumentNullException(nameof(@event));
        }

        var eventType = typeof(TEvent).Name;
        var actualExchange = exchange ?? _options.DefaultExchange;
        var actualRoutingKey = routingKey ?? eventType;

        IChannel? channel = null;
        var confirmationTracker = new PublishConfirmationTracker();
        var publishSucceeded = false;

        try
        {
            channel = await _connectionPool.GetChannelAsync(cancellationToken);

            channel.BasicAcksAsync += confirmationTracker.OnAckAsync;
            channel.BasicNacksAsync += confirmationTracker.OnNackAsync;
            channel.BasicReturnAsync += confirmationTracker.OnReturnAsync;

            await channel.ExchangeDeclareAsync(
                exchange: actualExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            var envelope = CreateEnvelope(@event, eventType, correlationId, headers);
            var body = SerializeEnvelope(envelope);

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = Guid.NewGuid().ToString(),
                CorrelationId = correlationId,
                Type = eventType,
                Timestamp = new AmqpTimestamp(envelope.Timestamp.Ticks),
                ContentType = "application/json"
            };

            var sequenceNumber = await channel.GetNextPublishSequenceNumberAsync(cancellationToken);
            confirmationTracker.ExpectSequence(sequenceNumber);
            confirmationTracker.RegisterMessageId(properties.MessageId, sequenceNumber);

            await channel.BasicPublishAsync(
                exchange: actualExchange,
                routingKey: actualRoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            var confirmTimeout = TimeSpan.FromSeconds(_options.PublisherConfirmTimeoutSeconds);
            var result = await confirmationTracker.WaitForConfirmationAsync(sequenceNumber, confirmTimeout, cancellationToken);

            if (!result.Confirmed)
            {
                var message = result.Returned
                    ? $"Message returned for event type '{eventType}' - broker could not route message"
                    : $"Publisher confirmation failed (nacked) for event type '{eventType}'";

                throw new RabbitMqPublishException(message, eventType, correlationId);
            }

            publishSucceeded = true;

            _logger.LogDebug(
                "Published event {EventType} with headers to exchange {Exchange}",
                eventType, actualExchange);
        }
        catch (RabbitMqPublishException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} with headers", eventType);
            throw new RabbitMqPublishException(
                $"Failed to publish event of type '{eventType}' with headers",
                eventType,
                correlationId,
                ex);
        }
        finally
        {
            if (channel != null)
            {
                channel.BasicAcksAsync -= confirmationTracker.OnAckAsync;
                channel.BasicNacksAsync -= confirmationTracker.OnNackAsync;
                channel.BasicReturnAsync -= confirmationTracker.OnReturnAsync;

                if (publishSucceeded)
                {
                    _connectionPool.ReturnChannel(channel);
                }
                else
                {
                    // Channel is disposed on error (not returned to pool)
                    channel.Dispose();
                }
            }
        }
    }

    private RabbitMqMessageEnvelope CreateEnvelope<TEvent>(
        TEvent @event,
        string eventType,
        string? correlationId,
        Dictionary<string, string?>? headers = null)
    {
        var payload = JsonSerializer.Serialize(@event, typeof(TEvent), _jsonSerializerContext ?? RabbitMqMessageEnvelopeContext.Default);

        var envelope = new RabbitMqMessageEnvelope(eventType, payload, headers ?? new Dictionary<string, string?>())
        {
            Timestamp = DateTime.UtcNow
        };

        if (!string.IsNullOrEmpty(correlationId))
        {
            envelope.Headers["CorrelationId"] = correlationId;
        }

        return envelope;
    }

    private ReadOnlyMemory<byte> SerializeEnvelope(RabbitMqMessageEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tracks publisher confirmations for a single message using RabbitMQ.Client 7.x async events.
    /// </summary>
    private sealed class PublishConfirmationTracker
    {
        private readonly ConcurrentDictionary<ulong, PublishResult> _results = new();
        private readonly ConcurrentDictionary<ulong, bool> _expectedSequences = new();
        private readonly ConcurrentDictionary<string, ulong> _messageIdToSequence = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void ExpectSequence(ulong sequenceNumber)
        {
            _expectedSequences[sequenceNumber] = true;
        }

        public void RegisterMessageId(string messageId, ulong sequenceNumber)
        {
            _messageIdToSequence[messageId] = sequenceNumber;
        }

        public Task OnAckAsync(object? sender, BasicAckEventArgs e)
        {
            // Multiple flag indicates batch acknowledgment
            if (e.Multiple)
            {
                // Iterate through all expected sequences and confirm those <= delivery tag
                foreach (var seq in _expectedSequences.Keys)
                {
                    if (seq <= e.DeliveryTag)
                    {
                        _results[seq] = new PublishResult { Confirmed = true, Returned = false };
                        _signal.Release();
                    }
                }
            }
            else
            {
                // Single message acknowledgment
                _results[e.DeliveryTag] = new PublishResult { Confirmed = true, Returned = false };
                _signal.Release();
            }
            return Task.CompletedTask;
        }

        public Task OnNackAsync(object? sender, BasicNackEventArgs e)
        {
            if (e.Multiple)
            {
                // Iterate through all expected sequences and nack those <= delivery tag
                foreach (var seq in _expectedSequences.Keys)
                {
                    if (seq <= e.DeliveryTag)
                    {
                        _results[seq] = new PublishResult { Confirmed = false, Returned = false };
                        _signal.Release();
                    }
                }
            }
            else
            {
                _results[e.DeliveryTag] = new PublishResult { Confirmed = false, Returned = false };
                _signal.Release();
            }
            return Task.CompletedTask;
        }

        public Task OnReturnAsync(object? sender, BasicReturnEventArgs e)
        {
            // Message was returned because it couldn't be routed (mandatory flag)
            // Extract message ID from basic properties and map to sequence number
            var messageId = e.BasicProperties?.MessageId;
            if (!string.IsNullOrEmpty(messageId) && _messageIdToSequence.TryGetValue(messageId, out var sequenceNumber))
            {
                _results[sequenceNumber] = new PublishResult { Confirmed = false, Returned = true };
                _signal.Release();
            }
            return Task.CompletedTask;
        }

        public async Task<PublishResult> WaitForConfirmationAsync(
            ulong sequenceNumber,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            try
            {
                // Wait for signal that confirmation arrived
                await _signal.WaitAsync(linkedCts.Token);

                // Use TryRemove to prevent memory leak
                if (_results.TryRemove(sequenceNumber, out var result))
                {
                    // Cleanup tracking dictionaries
                    _expectedSequences.TryRemove(sequenceNumber, out _);
                    return result;
                }

                // If not found, wait a bit more in case it was batched
                await _signal.WaitAsync(linkedCts.Token);
                if (_results.TryRemove(sequenceNumber, out result))
                {
                    _expectedSequences.TryRemove(sequenceNumber, out _);
                    return result;
                }
                return new PublishResult { Confirmed = false };
            }
            catch (OperationCanceledException)
            {
                // Timeout waiting for confirmation
                return new PublishResult { Confirmed = false };
            }
        }
    }

    private sealed class PublishResult
    {
        public bool Confirmed { get; init; }
        public bool Returned { get; init; }
    }
}