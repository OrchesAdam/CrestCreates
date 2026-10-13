using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CrestCreates.EventBus.RabbitMQ.Composition;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.EventBus.RabbitMQ.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Queue-plan consumer: one physical queue per consumer, handlers execute sequentially inside one async
/// DI scope per delivery, all handlers must succeed before the delivery is acked. Business results and
/// broker settlement are separate: decode/identity failures reject straight to the DLX; handler failures
/// consume the redelivery budget through an independent publisher channel; terminal states never throw
/// into the SDK dispatcher. The dedicated consumer channel is never part of the publisher pool.
/// </summary>
public sealed class RabbitMqConsumer : BackgroundService
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ConsumerChannelProbeInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConsumerChannelLossGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequeueBackoffFloor = TimeSpan.FromSeconds(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqCompositionReadiness _readiness;
    private readonly RabbitMqConnectionPool _connectionPool;
    private readonly RabbitMqPublisher _publisher;
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RabbitMqConsumer> _logger;

    private readonly SemaphoreSlim _settlementGate = new(1, 1);
    private readonly List<string> _consumerTags = new();
    private IChannel? _consumerChannel;
    private CancellationTokenSource? _deliveryCts;
    private CancellationToken _deliveryToken;
    private IRabbitMqBindingRegistry? _registry;
    private int _inFlightCount;
    private TaskCompletionSource _idle = CreateCompletedSource();

    public RabbitMqConsumer(
        IServiceProvider serviceProvider,
        RabbitMqCompositionReadiness readiness,
        RabbitMqConnectionPool connectionPool,
        RabbitMqPublisher publisher,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<RabbitMqConsumer> logger)
    {
        // The frozen registry is resolved only after the readiness gate: all IHostedService
        // instances are constructed before any StartAsync runs, so resolving the registry in the
        // constructor would freeze the bindings before the composition service built the event kernel.
        _serviceProvider = serviceProvider;
        _readiness = readiness;
        _connectionPool = connectionPool;
        _publisher = publisher;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _readiness.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _registry = _serviceProvider.GetRequiredService<IRabbitMqBindingRegistry>();

        if (_registry.PublisherOnly)
        {
            _logger.LogInformation("RabbitMQ host is publisher-only; the consumer does not start.");
            return;
        }

        if (_registry.QueuePlans.Count == 0)
        {
            _logger.LogError("RabbitMQ composition produced zero queue plans; the consumer does not start.");
            return;
        }

        _deliveryCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _deliveryToken = _deliveryCts.Token;

        try
        {
            _consumerChannel = await _connectionPool.CreateDedicatedChannelAsync(stoppingToken).ConfigureAwait(false);
            await DeclareDeadLetterExchangeAsync(_consumerChannel, stoppingToken).ConfigureAwait(false);

            foreach (var plan in _registry.QueuePlans)
            {
                await SetupQueueAsync(_consumerChannel, plan, stoppingToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "RabbitMQ consumer started with {Queues} queue plan(s) and {Handlers} handler binding(s).",
                _registry.QueuePlans.Count,
                _registry.QueuePlans.Sum(p => p.HandlersByIdentity.Count));

            await SuperviseConsumerChannelAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("RabbitMQ consumer shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in the RabbitMQ consumer; failing startup is intentional.");
            throw;
        }
    }

    /// <summary>
    /// Watches the dedicated consumer channel for terminal loss. While the SDK recovery loop owns the
    /// connection (a drop was observed, or the connection is not open), the SDK first reopens the
    /// underlying connection and only then restores topology/channels/consumers before signalling
    /// RecoverySucceeded — a closed channel in that phase is recovery work in progress and must not
    /// stop the host. Once the connection is stable and no recovery is running, a channel that stays
    /// closed was not restored (or was closed terminally): a short grace absorbs completion-event
    /// races, then the consumer fails the host instead of staying falsely ready.
    /// </summary>
    private async Task SuperviseConsumerChannelAsync(CancellationToken stoppingToken)
    {
        DateTime? lostSince = null;
        while (true)
        {
            await Task.Delay(ConsumerChannelProbeInterval, stoppingToken).ConfigureAwait(false);

            if (_consumerChannel is { IsOpen: true })
            {
                lostSince = null;
                continue;
            }

            if (_connectionPool.IsConnectionRecovering || _connectionPool.IsRecoveryInProgress)
            {
                lostSince = null;
                continue;
            }

            lostSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - lostSince.Value > ConsumerChannelLossGrace)
            {
                throw new RabbitMqConnectionException(
                    $"The dedicated consumer channel has been closed for more than {ConsumerChannelLossGrace.TotalSeconds}s " +
                    "without an active SDK recovery; failing the host instead of staying falsely ready.");
            }
        }
    }

    private async Task DeclareDeadLetterExchangeAsync(IChannel channel, CancellationToken cancellationToken)
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

    private async Task SetupQueueAsync(IChannel channel, RabbitMqQueuePlan plan, CancellationToken cancellationToken)
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
            consumer.ReceivedAsync += (_, args) => OnReceivedAsync(plan, args);

            var consumerTag = await channel.BasicConsumeAsync(
                queue: plan.Queue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _consumerTags.Add(consumerTag);

            _logger.LogInformation(
                "Consuming from queue {Queue} (prefetch {Prefetch}, {Identities} declared identity/identities, consumer tag {ConsumerTag})",
                plan.Queue, plan.PrefetchCount, plan.HandlersByIdentity.Count, consumerTag);
        }
        finally
        {
            _settlementGate.Release();
        }
    }

    private Task OnReceivedAsync(RabbitMqQueuePlan plan, BasicDeliverEventArgs args)
    {
        // The callback only copies the pooled body and hands off; handler execution, redelivery delays
        // and settlement happen outside the SDK dispatcher.
        var delivery = new DeliveryContext(
            plan,
            args.Body.ToArray(),
            RabbitMqPublisher.CloneProperties(args.BasicProperties),
            args.DeliveryTag);

        BeginDelivery();
        _ = ProcessDeliveryAsync(delivery, _deliveryToken);
        return Task.CompletedTask;
    }

    private async Task ProcessDeliveryAsync(DeliveryContext delivery, CancellationToken cancellationToken)
    {
        try
        {
            RabbitMqMessageEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize(
                    delivery.Body,
                    RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
            }
            catch (JsonException ex)
            {
                await RejectAsync(delivery, $"malformed envelope JSON ({ex.Message})").ConfigureAwait(false);
                return;
            }

            if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
            {
                await RejectAsync(delivery, "envelope has no event type").ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrEmpty(envelope.Payload))
            {
                // JSON envelopes may legally carry payload:null; without this check the typed
                // deserialize would throw ArgumentNullException, bypass every rejection branch and
                // leave the delivery unacknowledged forever.
                await RejectAsync(delivery, "envelope has no payload").ConfigureAwait(false);
                return;
            }

            var resolution = _registry!.ResolveWire(envelope.EventType, envelope.EventId, envelope.EventVersion);
            if (!resolution.Success)
            {
                await RejectAsync(delivery, resolution.FailureReason!).ConfigureAwait(false);
                return;
            }

            var binding = resolution.Binding!;
            var identity = new RabbitMqWireIdentity(binding.Descriptor.Id, binding.Descriptor.Version);
            if (!delivery.Plan.HandlersByIdentity.TryGetValue(identity, out var handlers) || handlers.Count == 0)
            {
                await RejectAsync(
                    delivery,
                    $"queue '{delivery.Plan.Queue}' does not declare event '{binding.Descriptor.Name}' v{binding.Descriptor.Version}").ConfigureAwait(false);
                return;
            }

            object? payload;
            try
            {
                payload = JsonSerializer.Deserialize(envelope.Payload, binding.Codec);
            }
            catch (JsonException ex)
            {
                await RejectAsync(delivery, $"payload JSON does not match '{binding.Descriptor.PayloadType.FullName}' ({ex.Message})").ConfigureAwait(false);
                return;
            }

            if (payload is null)
            {
                await RejectAsync(delivery, "payload deserialized to null").ConfigureAwait(false);
                return;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                // Resolve every handler for this delivery before running any of them: a missing
                // registration or a failing constructor must not leave partial business effects
                // (freeze already validates that every handler type is a registered service; this
                // guards scope-construction failures and keeps the delivered batch transactional in
                // the "all or nothing per attempt" sense).
                var handlerInstances = new object[handlers.Count];
                for (var index = 0; index < handlers.Count; index++)
                {
                    handlerInstances[index] = scope.ServiceProvider.GetRequiredService(handlers[index].HandlerType);
                }

                for (var index = 0; index < handlers.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await handlers[index].Invoker(handlerInstances[index], payload, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Delivery of '{EventName}' on queue {Queue} was cancelled during shutdown and remains unacknowledged.",
                    binding.Descriptor.Name, delivery.Plan.Queue);
                return;
            }
            catch (Exception ex)
            {
                await HandleBusinessFailureAsync(delivery, binding, ex, cancellationToken).ConfigureAwait(false);
                return;
            }

            await AckAsync(delivery, binding.Descriptor.Name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Last-resort guard: a delivery must never let an unhandled exception escape into the SDK
            // dispatcher or trigger implicit broker-side redelivery loops.
            _logger.LogError(
                ex,
                "Unhandled delivery processing error on queue {Queue} (delivery tag {DeliveryTag}); leaving the message unacknowledged.",
                delivery.Plan.Queue, delivery.DeliveryTag);
        }
        finally
        {
            EndDelivery();
        }
    }

    private async Task HandleBusinessFailureAsync(
        DeliveryContext delivery,
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
            await RejectAsync(delivery, $"invalid retry header: {headerProblem}").ConfigureAwait(false);
            return;
        }

        if (retryCount >= _options.RetryCount)
        {
            _logger.LogWarning(
                handlerException,
                "Handler failures exhausted the retry budget ({RetryCount}) for '{EventName}' on queue {Queue}; " +
                "the message is dead-lettered.",
                _options.RetryCount, binding.Descriptor.Name, delivery.Plan.Queue);
            await RejectAsync(delivery, "retry budget exhausted").ConfigureAwait(false);
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
            await RequeueOriginalAsync(delivery, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Acknowledge the original delivery only after the redelivery was confirmed by the broker.
        await AckAsync(delivery, binding.Descriptor.Name).ConfigureAwait(false);
    }

    /// <summary>
    /// Requeues the original delivery after a settlement failure. Never spins: the requeue waits at
    /// least the backoff floor (or the configured retry delay when larger) so a persistently failing
    /// broker cannot turn into a hot loop, and shutdown cancellation still leaves the delivery
    /// unacknowledged (broker-side requeue semantics apply).
    /// </summary>
    private async Task RequeueOriginalAsync(DeliveryContext delivery, CancellationToken cancellationToken)
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

        if (_consumerChannel is null)
        {
            return;
        }

        await _settlementGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _consumerChannel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
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

    private async Task AckAsync(DeliveryContext delivery, string eventName)
    {
        if (_consumerChannel is null)
        {
            return;
        }

        await _settlementGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _consumerChannel.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
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

    private async Task RejectAsync(DeliveryContext delivery, string reason)
    {
        _logger.LogWarning(
            "Rejecting message on queue {Queue} (delivery tag {DeliveryTag}, message id {MessageId}): {Reason}",
            delivery.Plan.Queue, delivery.DeliveryTag, delivery.Properties.MessageId, reason);

        if (_consumerChannel is null)
        {
            return;
        }

        await _settlementGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _consumerChannel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false).ConfigureAwait(false);
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

    private void BeginDelivery()
    {
        if (Interlocked.Increment(ref _inFlightCount) == 1)
        {
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void EndDelivery()
    {
        if (Interlocked.Decrement(ref _inFlightCount) == 0)
        {
            _idle.TrySetResult();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Shutdown order: stop intake, bounded wait for in-flight deliveries, then close the dedicated
        // consumer channel (unfinished messages stay unacknowledged), then let the base class cancel
        // the execution token. The consumer channel is never returned to the publisher pool.
        if (_registry is null || _registry.PublisherOnly || _consumerChannel is null)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("Stopping RabbitMQ consumer: cancelling intake...");
        await _settlementGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            foreach (var tag in _consumerTags)
            {
                try
                {
                    await _consumerChannel.BasicCancelAsync(tag, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cancel consumer {ConsumerTag}.", tag);
                }
            }
        }
        finally
        {
            _settlementGate.Release();
        }

        if (Volatile.Read(ref _inFlightCount) > 0)
        {
            var drained = await Task.WhenAny(_idle.Task, Task.Delay(ShutdownDrainTimeout, CancellationToken.None))
                .ConfigureAwait(false);
            if (drained != _idle.Task)
            {
                _logger.LogWarning(
                    "In-flight deliveries did not drain within {Timeout}s; closing the consumer channel with " +
                    "{Count} delivery/deliveries unfinished. They remain unacknowledged and will be redelivered.",
                    ShutdownDrainTimeout.TotalSeconds, Volatile.Read(ref _inFlightCount));
            }
        }

        try
        {
            await _consumerChannel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while closing the dedicated consumer channel.");
        }

        _consumerChannel.Dispose();
        _consumerChannel = null;

        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Cancelled through the linked parent token by base.StopAsync; dispose only after that so any
        // straggler delivery still observes cancellation instead of an ObjectDisposedException.
        _deliveryCts?.Dispose();
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed record DeliveryContext(
        RabbitMqQueuePlan Plan,
        byte[] Body,
        BasicProperties Properties,
        ulong DeliveryTag);
}
