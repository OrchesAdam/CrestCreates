using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using CrestCreates.EventBus.RabbitMQ.Options;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Broker settlement for consumer deliveries: ack, reject-to-DLX and requeue-after-settlement-failure.
/// The channel is read through an accessor because the consumer owns its lifecycle; all operations
/// share the consumer's settlement gate so they never interleave with topology changes or shutdown.
/// </summary>
internal sealed class RabbitMqDeliverySettlement
{
    private static readonly TimeSpan RequeueBackoffFloor = TimeSpan.FromSeconds(1);

    private readonly Func<IChannel?> _channelAccessor;
    private readonly SemaphoreSlim _settlementGate;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumer> _logger;

    public RabbitMqDeliverySettlement(
        Func<IChannel?> channelAccessor,
        SemaphoreSlim settlementGate,
        RabbitMqOptions options,
        ILogger<RabbitMqConsumer> logger)
    {
        _channelAccessor = channelAccessor;
        _settlementGate = settlementGate;
        _options = options;
        _logger = logger;
    }

    public async Task AckAsync(RabbitMqConsumer.DeliveryContext delivery, string eventName)
    {
        var channel = _channelAccessor();
        if (channel is null)
        {
            return;
        }

        await _settlementGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
            _logger.LogDebug("Acknowledged '{EventName}' (delivery tag {DeliveryTag}).", eventName, delivery.DeliveryTag);
        }
        catch (Exception ex)
        {
            // Business succeeded, settlement did not confirm: this is a distinct diagnostic, not a
            // business failure. The channel/connection recovery path may redeliver the message.
            _logger.LogError(
                ex,
                "Business processing of '{EventName}' succeeded but the broker acknowledgement failed " +
                "(delivery tag {DeliveryTag}); the message may be redelivered.",
                eventName, delivery.DeliveryTag);
        }
        finally
        {
            _settlementGate.Release();
        }
    }

    public async Task RejectAsync(RabbitMqConsumer.DeliveryContext delivery, string reason)
    {
        _logger.LogWarning(
            "Rejecting message on queue {Queue} (delivery tag {DeliveryTag}, message id {MessageId}): {Reason}",
            delivery.Plan.Queue, delivery.DeliveryTag, delivery.Properties.MessageId, reason);

        var channel = _channelAccessor();
        if (channel is null)
        {
            return;
        }

        await _settlementGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Rejection (nack) failed on queue {Queue} (delivery tag {DeliveryTag}); broker-side recovery applies.",
                delivery.Plan.Queue, delivery.DeliveryTag);
        }
        finally
        {
            _settlementGate.Release();
        }
    }

    /// <summary>
    /// Requeues the original delivery after a settlement failure. Never spins: the requeue waits at
    /// least the backoff floor (or the configured retry delay when larger) so a persistently failing
    /// broker cannot turn into a hot loop, and shutdown cancellation still leaves the delivery
    /// unacknowledged (broker-side requeue semantics apply).
    /// </summary>
    public async Task RequeueOriginalAsync(RabbitMqConsumer.DeliveryContext delivery, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(Math.Max(RequeueBackoffFloor.TotalSeconds, _options.RetryDelaySeconds));
        try
        {
            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Requeue of the unacknowledged delivery on queue {Queue} was cancelled during shutdown; the message stays unacknowledged.",
                delivery.Plan.Queue);
            return;
        }

        var channel = _channelAccessor();
        if (channel is null)
        {
            return;
        }

        await _settlementGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
            _logger.LogWarning(
                "Requeued the unacknowledged delivery on queue {Queue} (delivery tag {DeliveryTag}) after a " +
                "settlement failure.",
                delivery.Plan.Queue, delivery.DeliveryTag);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Requeue (nack with requeue) failed on queue {Queue} (delivery tag {DeliveryTag}); broker-side " +
                "recovery semantics apply.",
                delivery.Plan.Queue, delivery.DeliveryTag);
        }
        finally
        {
            _settlementGate.Release();
        }
    }
}
