using System;
using System.Collections.Generic;
using System.Linq;
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

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Queue-plan consumer: one physical queue per consumer, handlers execute sequentially inside one async
/// DI scope per delivery, all handlers must succeed before the delivery is acked. Business results and
/// broker settlement are separate: decode/identity failures reject straight to the DLX; handler failures
/// consume the redelivery budget through an independent publisher channel; terminal states never throw
/// into the SDK dispatcher. The dedicated consumer channel is never part of the publisher pool.
/// Topology, delivery processing, settlement and retry policy are internal collaborators; this class
/// owns the channel lifecycle, supervision, drain and delivery dispatch.
/// </summary>
public sealed class RabbitMqConsumer : BackgroundService
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ConsumerChannelProbeInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConsumerChannelLossGrace = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqCompositionReadiness _readiness;
    private readonly RabbitMqConnectionPool _connectionPool;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly RabbitMqConsumerTopology _topology;
    private readonly RabbitMqDeliveryProcessor _processor;
    private readonly RabbitMqDeliverySettlement _settlement;
    private readonly RabbitMqFailureSettlement _failureSettlement;

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
        _options = options.Value;
        _logger = logger;
        _topology = new RabbitMqConsumerTopology(_options, _settlementGate, logger);
        _settlement = new RabbitMqDeliverySettlement(() => _consumerChannel, _settlementGate, _options, logger);
        _failureSettlement = new RabbitMqFailureSettlement(_options, publisher, _settlement, logger);
        _processor = new RabbitMqDeliveryProcessor(() => _registry, scopeFactory, logger);
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
            await _topology.DeclareDeadLetterExchangeAsync(_consumerChannel, stoppingToken).ConfigureAwait(false);

            foreach (var plan in _registry.QueuePlans)
            {
                _consumerTags.Add(await _topology
                    .SetupQueueAsync(_consumerChannel, plan, OnReceivedAsync, stoppingToken)
                    .ConfigureAwait(false));
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
            var outcome = await _processor.ProcessAsync(delivery, cancellationToken).ConfigureAwait(false);
            switch (outcome)
            {
                case RabbitMqDeliveryOutcome.Rejected rejected:
                    await _settlement.RejectAsync(delivery, rejected.Reason).ConfigureAwait(false);
                    break;
                case RabbitMqDeliveryOutcome.BusinessFailure failure:
                    await _failureSettlement
                        .HandleAsync(delivery, failure.Binding, failure.Exception, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case RabbitMqDeliveryOutcome.Acked acked:
                    await _settlement.AckAsync(delivery, acked.EventName).ConfigureAwait(false);
                    break;
                case RabbitMqDeliveryOutcome.Cancelled:
                    return;
            }
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

    internal sealed record DeliveryContext(
        RabbitMqQueuePlan Plan,
        byte[] Body,
        BasicProperties Properties,
        ulong DeliveryTag);
}
