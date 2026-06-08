using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local.Channel;

public sealed class LocalDeadLetterBackgroundService : BackgroundService
{
    private readonly ILocalDeadLetterStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDeadLetterBackgroundService> _logger;
    private readonly LocalDeadLetterOptions _options;

    public LocalDeadLetterBackgroundService(
        ILocalDeadLetterStore store,
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDeadLetterBackgroundService> logger,
        IOptions<LocalDeadLetterOptions> options)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.RetryIntervalSeconds),
                    stoppingToken);

                var pending = await _store.ListAsync(
                    status: DeadLetterStatus.Pending,
                    take: 100,
                    cancellationToken: stoppingToken);

                foreach (var message in pending)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    if (message.RetryCount >= _options.MaxRetries)
                    {
                        _logger.LogWarning(
                            "Dead letter message {MessageId} of type {EventType} has reached max retries ({RetryCount}/{MaxRetries}), archiving",
                            message.MessageId, message.EventType, message.RetryCount, _options.MaxRetries);
                        continue;
                    }

                    await _store.MarkRetryingAsync(message.MessageId, stoppingToken);

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();

                        var eventType = Type.GetType(message.EventType);
                        if (eventType is null)
                        {
                            _logger.LogError(
                                "Cannot resolve event type {EventType} for dead letter message {MessageId}",
                                message.EventType, message.MessageId);
                            continue;
                        }

                        var eventData = JsonSerializer.Deserialize(
                            message.Payload, eventType);

                        if (eventData is ILocalEvent localEvent)
                        {
                            await dispatcher.DispatchAsync(localEvent, stoppingToken);
                        }

                        await _store.MarkRetriedAsync(message.MessageId, stoppingToken);
                        _logger.LogInformation(
                            "Successfully retried dead letter message {MessageId} of type {EventType}",
                            message.MessageId, message.EventType);
                    }
                    catch (Exception ex)
                    {
                        var newRetryCount = message.RetryCount + 1;
                        _logger.LogError(ex,
                            "Retry {RetryCount}/{MaxRetries} failed for dead letter message {MessageId} of type {EventType}",
                            newRetryCount, _options.MaxRetries, message.MessageId, message.EventType);

                        var updatedMessage = message with
                        {
                            RetryCount = newRetryCount,
                            Status = newRetryCount >= _options.MaxRetries
                                ? DeadLetterStatus.Archived
                                : DeadLetterStatus.Pending
                        };
                        await _store.EnqueueAsync(updatedMessage, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in dead letter background retry loop");
            }
        }
    }
}
