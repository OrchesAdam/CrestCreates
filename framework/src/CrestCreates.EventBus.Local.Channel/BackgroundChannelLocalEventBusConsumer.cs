using System;
using System.Text.Json;
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
                await ProcessEventAsync(@event, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessEventAsync(ILocalEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();
            await dispatcher.DispatchAsync(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing local event of type {EventType}", @event.GetType().Name);

            // Try to enqueue to DLQ if available
            try
            {
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var deadLetterStore = scope.ServiceProvider.GetService<ILocalDeadLetterStore>();
                if (deadLetterStore is not null)
                {
                    var eventType = @event.GetType();
                    var payload = JsonSerializer.SerializeToUtf8Bytes(@event, eventType);

                    var message = new DeadLetterMessage(
                        MessageId: Guid.NewGuid().ToString("N"),
                        EventType: eventType.AssemblyQualifiedName!,
                        Payload: payload,
                        ErrorMessage: ex.Message,
                        FailedAt: DateTime.UtcNow,
                        RetryCount: 0,
                        MaxRetries: 3,
                        Status: DeadLetterStatus.Pending);

                    await deadLetterStore.EnqueueAsync(message, cancellationToken);
                }
            }
            catch (Exception dlqEx)
            {
                _logger.LogError(dlqEx, "Failed to enqueue event to dead letter store");
            }
        }
    }
}
