using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local.Channel;

/// <summary>
/// Background scheduling for dead-letter replay: polling cadence, batch size and shutdown only.
/// Every attempt delegates to the shared replay executor — decoding, state transitions and
/// dispatch are never duplicated here.
/// </summary>
public sealed class LocalDeadLetterBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILocalDeadLetterReplayExecutor _executor;
    private readonly LocalDeadLetterOptions _options;
    private readonly ILogger<LocalDeadLetterBackgroundService>? _logger;

    public LocalDeadLetterBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILocalDeadLetterReplayExecutor executor,
        IOptions<LocalDeadLetterOptions>? options = null,
        ILogger<LocalDeadLetterBackgroundService>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _executor = executor;
        _options = options?.Value ?? new LocalDeadLetterOptions();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.RetryIntervalSeconds),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                IReadOnlyList<DeadLetterMessage> pending;
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var store = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
                    pending = await store.GetPendingAsync(0, 100, stoppingToken).ConfigureAwait(false);
                }

                foreach (var message in pending)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    try
                    {
                        await _executor.ExecuteAsync(message, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in dead letter background retry loop");
            }
        }
    }
}
