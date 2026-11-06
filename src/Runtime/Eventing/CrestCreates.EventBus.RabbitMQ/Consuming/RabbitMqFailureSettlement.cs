using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Registration;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Failure settlement for a delivery whose business handlers failed: reads the framework retry
/// header, rejects invalid/exhausted messages to the DLX, and otherwise publishes a redelivery copy
/// before acknowledging the original. A failed retry publish requeues the original instead of
/// consuming the business budget.
/// </summary>
internal sealed class RabbitMqFailureSettlement
{
    private readonly RabbitMqOptions _options;
    private readonly RabbitMqPublisher _publisher;
    private readonly RabbitMqDeliverySettlement _settlement;
    private readonly ILogger<RabbitMqConsumer> _logger;

    public RabbitMqFailureSettlement(
        RabbitMqOptions options,
        RabbitMqPublisher publisher,
        RabbitMqDeliverySettlement settlement,
        ILogger<RabbitMqConsumer> logger)
    {
        _options = options;
        _publisher = publisher;
        _settlement = settlement;
        _logger = logger;
    }

    public async Task HandleAsync(
        RabbitMqConsumer.DeliveryContext delivery,
        RabbitMqPublishBinding binding,
        Exception handlerException,
        CancellationToken cancellationToken)
    {
        if (!RabbitMqRetryHeader.TryRead(delivery.Properties, out var retryCount, out var headerProblem))
        {
            _logger.LogError(
                handlerException,
                "Handler failure for '{EventName}' on queue {Queue} coincided with an invalid retry header: {Problem}. " +
                "The message is isolated to the dead-letter path without consuming the retry budget.",
                binding.Descriptor.Name, delivery.Plan.Queue, headerProblem);
            await _settlement.RejectAsync(delivery, $"invalid retry header: {headerProblem}").ConfigureAwait(false);
            return;
        }

        if (retryCount >= _options.RetryCount)
        {
            _logger.LogWarning(
                handlerException,
                "Handler failures exhausted the retry budget ({RetryCount}) for '{EventName}' on queue {Queue}; " +
                "the message is dead-lettered.",
                _options.RetryCount, binding.Descriptor.Name, delivery.Plan.Queue);
            await _settlement.RejectAsync(delivery, "retry budget exhausted").ConfigureAwait(false);
            return;
        }

        var nextRetryCount = retryCount + 1;
        _logger.LogWarning(
            handlerException,
            "Handler failure for '{EventName}' on queue {Queue}; scheduling redelivery attempt {Attempt}/{Max} after {Delay}s.",
            binding.Descriptor.Name, delivery.Plan.Queue, nextRetryCount, _options.RetryCount, _options.RetryDelaySeconds);

        try
        {
            if (_options.RetryDelaySeconds > 0)
            {
                // Cancellable in-process wait; the original delivery stays unacknowledged. This is a
                // nominal wait, not a durable scheduler and not a strict time guarantee.
                await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), cancellationToken).ConfigureAwait(false);
            }

            var headers = new Dictionary<string, object?>();
            if (delivery.Properties.Headers is not null)
            {
                foreach (var pair in delivery.Properties.Headers)
                {
                    headers[pair.Key] = pair.Value;
                }
            }

            headers[RabbitMqRetryHeader.Key] = nextRetryCount.ToString(CultureInfo.InvariantCulture);

            var redeliveryProperties = RabbitMqPublisher.CloneProperties(delivery.Properties, headers);
            await _publisher.RepublishToQueueAsync(delivery.Plan.Queue, delivery.Body, redeliveryProperties, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Redelivery of '{EventName}' on queue {Queue} was cancelled during shutdown; the original delivery remains unacknowledged.",
                binding.Descriptor.Name, delivery.Plan.Queue);
            return;
        }
        catch (RabbitMqPublishException ex)
        {
            _logger.LogError(
                ex,
                "Redelivery of '{EventName}' on queue {Queue} failed ({Kind}); the original delivery is requeued " +
                "for a controlled recovery (the business retry budget is not consumed by settlement failures).",
                binding.Descriptor.Name, delivery.Plan.Queue, ex.FailureKind);

            // A publish-confirm failure on a healthy consumer channel does not invalidate the delivery:
            // leaving it unacknowledged would silently occupy a prefetch slot forever. Requeue the
            // original message for a bounded-backoff recovery instead (no header increment on the
            // framework-owned retry counter; the outcome of the failed publish is unknown, so a
            // duplicate window is preserved).
            await _settlement.RequeueOriginalAsync(delivery, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Acknowledge the original delivery only after the redelivery was confirmed by the broker.
        await _settlement.AckAsync(delivery, binding.Descriptor.Name).ConfigureAwait(false);
    }
}
