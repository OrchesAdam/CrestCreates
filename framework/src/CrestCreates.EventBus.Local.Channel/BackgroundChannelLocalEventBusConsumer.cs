using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local.Channel;

public sealed class BackgroundChannelLocalEventBusConsumer : BackgroundService
{
    private readonly ChannelLocalEventQueue _queue;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<BackgroundChannelLocalEventBusConsumer> _logger;

    public BackgroundChannelLocalEventBusConsumer(
        ChannelLocalEventQueue queue,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<BackgroundChannelLocalEventBusConsumer> logger)
    {
        _queue = queue;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var @event in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();
                    await dispatcher.DispatchAsync(@event, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to dispatch local event {EventType}.", @event.GetType().FullName);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
