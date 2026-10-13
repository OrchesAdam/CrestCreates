using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local.Channel;

/// <summary>
/// Background consumer of the channel queue. Each event dispatches in a fresh scope through the
/// unified dispatcher; a handler failure is recorded through the shared typed failure recorder
/// and the loop continues — in contrast to the synchronous bus it never rethrows to a caller.
/// </summary>
public sealed class BackgroundChannelLocalEventBusConsumer : BackgroundService
{
    private readonly ChannelLocalEventQueue _queue;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILocalEventDispatchRegistry _registry;
    private readonly ILocalEventFailureRecorder? _recorder;
    private readonly ILogger<BackgroundChannelLocalEventBusConsumer>? _logger;

    public BackgroundChannelLocalEventBusConsumer(
        ChannelLocalEventQueue queue,
        IServiceScopeFactory serviceScopeFactory,
        ILocalEventDispatchRegistry registry,
        ILocalEventFailureRecorder? recorder = null,
        ILogger<BackgroundChannelLocalEventBusConsumer>? logger = null)
    {
        _queue = queue;
        _serviceScopeFactory = serviceScopeFactory;
        _registry = registry;
        _recorder = recorder;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var @event in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessEventAsync(@event, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessEventAsync(ILocalEvent @event, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();
            await dispatcher.DispatchAsync(@event, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: do not record a dead letter for a canceled dispatch.
        }
        catch (Exception failure)
        {
            _logger?.LogError(failure, "Error processing local event of type {EventType}", @event.GetType().Name);

            if (_recorder is null || stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!_registry.TryGet(@event.GetType(), out var binding))
            {
                _logger?.LogWarning(
                    "Failed local event '{EventType}' has no dispatch registration; nothing was recorded.",
                    @event.GetType().FullName);
                return;
            }

            try
            {
                var outcome = await _recorder
                    .TryRecordAsync(binding, @event, failure, stoppingToken)
                    .ConfigureAwait(false);

                if (outcome.Recorded)
                {
                    _logger?.LogInformation(
                        "Recorded failed channel local event '{EventName}' to the dead-letter store.",
                        binding.LocalName);
                }
                else if (outcome.Problem is not null)
                {
                    _logger?.LogWarning(
                        "Channel local event '{EventName}' failed and was not recorded to the dead-letter store: {Problem}",
                        binding.LocalName, outcome.Problem);
                }
            }
            catch (Exception recordFailure)
            {
                _logger?.LogError(recordFailure, "Failed to record event to dead letter store");
            }
        }
    }
}
