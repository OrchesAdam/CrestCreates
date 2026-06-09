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
    private readonly IDeadLetterStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDeadLetterBackgroundService> _logger;
    private readonly LocalDeadLetterOptions _options;

    public LocalDeadLetterBackgroundService(
        IDeadLetterStore store,
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

                var pending = await _store.GetPendingAsync(0, 100, stoppingToken);

                foreach (var message in pending)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    if (message.RetryCount >= _options.MaxRetries)
                    {
                        _logger.LogWarning(
                            "Dead letter message {MessageId} ({EventName}:v{EventVersion}) has reached max retries ({RetryCount}/{MaxRetries}), archiving",
                            message.MessageId, message.EventName, message.EventVersion, message.RetryCount, _options.MaxRetries);
                        var archived = message with { Status = DeadLetterStatus.Archived };
                        await _store.EnqueueAsync(archived, stoppingToken);
                        continue;
                    }

                    await _store.MarkRetryingAsync(message.MessageId, stoppingToken);

                    try
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();

                        var eventType = Type.GetType(message.PayloadTypeFullName);
                        if (eventType is null)
                        {
                            _logger.LogError(
                                "Cannot resolve event type {PayloadTypeFullName} for dead letter message {MessageId}",
                                message.PayloadTypeFullName, message.MessageId);
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
                            "Successfully retried dead letter message {MessageId} ({EventName}:v{EventVersion})",
                            message.MessageId, message.EventName, message.EventVersion);
                    }
                    catch (Exception ex)
                    {
                        var newRetryCount = message.RetryCount + 1;
                        _logger.LogError(ex,
                            "Retry {RetryCount}/{MaxRetries} failed for dead letter message {MessageId} ({EventName}:v{EventVersion})",
                            newRetryCount, _options.MaxRetries, message.MessageId, message.EventName, message.EventVersion);

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
