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
    private static readonly TimeSpan ShutdownBrokerTimeout = TimeSpan.FromSeconds(5);
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
    private readonly object _deliveryGate = new();
    private readonly TaskCompletionSource _startupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _consumerTags = new();
    private IChannel? _consumerChannel;
    private CancellationTokenSource? _deliveryCts;
    private CancellationToken _deliveryToken;
    private IRabbitMqBindingRegistry? _registry;
    private int _inFlightCount;
    private bool _acceptDeliveries = true;
    private int _stopStarted;
    private readonly TaskCompletionSource _stopCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

            stoppingToken.ThrowIfCancellationRequested();
            _deliveryCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _deliveryToken = _deliveryCts.Token;
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

            _startupCompleted.TrySetResult();
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
        finally
        {
            _startupCompleted.TrySetResult();
            if (stoppingToken.IsCancellationRequested)
            {
                await CloseConsumerChannelAsync().ConfigureAwait(false);
            }
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
            if (Volatile.Read(ref _stopStarted) != 0)
                return;

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

        if (TryBeginDelivery())
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

    private bool TryBeginDelivery()
    {
        lock (_deliveryGate)
        {
            if (!_acceptDeliveries)
                return false;

            if (++_inFlightCount == 1)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    private void EndDelivery()
    {
        lock (_deliveryGate)
        {
            if (--_inFlightCount == 0)
                _idle.TrySetResult();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _stopStarted, 1, 0) == 0)
            _ = StopOnceAsync();

        try
        {
            await _stopCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopOnceAsync()
    {
        try
        {
            await DrainAndStopAsync().ConfigureAwait(false);
            _stopCompleted.TrySetResult();
        }
        catch (Exception exception)
        {
            _stopCompleted.TrySetException(exception);
        }
    }

    private async Task DrainAndStopAsync()
    {
        Task idle;
        lock (_deliveryGate)
        {
            _acceptDeliveries = false;
            idle = _idle.Task;
        }

        try
        {
            if (ExecuteTask is null)
                return;

            try
            {
                await _startupCompleted.Task.WaitAsync(ShutdownBrokerTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("RabbitMQ consumer startup did not finish before shutdown; cancelling startup.");
                return;
            }

            if (_consumerChannel is not { } channel)
                return;

            await CancelIntakeAsync(channel).ConfigureAwait(false);
            try
            {
                await idle.WaitAsync(ShutdownDrainTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "In-flight deliveries did not drain within {Timeout}s; unfinished deliveries will be redelivered.",
                    ShutdownDrainTimeout.TotalSeconds);
            }

            await CloseConsumerChannelAsync().ConfigureAwait(false);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(ShutdownBrokerTimeout);
            await base.StopAsync(timeout.Token).ConfigureAwait(false);
            Interlocked.Exchange(ref _deliveryCts, null)?.Dispose();
        }
    }

    private async Task CancelIntakeAsync(IChannel channel)
    {
        using var timeout = new CancellationTokenSource(ShutdownBrokerTimeout);
        try
        {
            await _settlementGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                foreach (var tag in _consumerTags)
                    await channel.BasicCancelAsync(tag, cancellationToken: timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _settlementGate.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not cancel RabbitMQ consumer intake before closing its channel.");
        }
    }

    private async Task CloseConsumerChannelAsync()
    {
        if (Interlocked.Exchange(ref _consumerChannel, null) is not { } channel)
            return;

        using var timeout = new CancellationTokenSource(ShutdownBrokerTimeout);
        try
        {
            await channel.CloseAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Error while closing the dedicated consumer channel.");
        }

        try
        {
            await channel.DisposeAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Error while disposing the dedicated consumer channel.");
        }
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
