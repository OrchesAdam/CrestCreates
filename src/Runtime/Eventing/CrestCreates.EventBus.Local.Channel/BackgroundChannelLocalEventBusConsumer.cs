using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local.Channel;

/// <summary>
/// Background consumer of the channel queue. Each work item dispatches in a fresh scope through the
/// shared admission/execution core: the captured context is restored (tenant resolution awaited,
/// ambient activation in this frame), the then-current policy is re-checked, the whole handler batch
/// is prepared once and the same instances execute. Handler completion is the execution fact:
/// releasing the scope and the context frame afterwards is reported independently and a release
/// fault never turns a completed delivery into a replayable business failure. Failures are recorded
/// through the shared recorder with the original work-item identity and context snapshot.
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
            await foreach (var workItem in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessEventAsync(workItem, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessEventAsync(LocalEventWorkItem workItem, CancellationToken stoppingToken)
    {
        var payload = workItem.Payload;
        Exception? dispatchFailure = null;
        var contextFrame = LocalEventContextFrame.Empty;
        var scope = _serviceScopeFactory.CreateAsyncScope();
        try
        {
            if (!_registry.TryGet(payload.GetType(), out var binding))
            {
                _logger?.LogWarning(
                    "Channel local event '{EventType}' has no dispatch registration; the work item was dropped.",
                    payload.GetType().FullName);
                await TryReleaseScopeAsync(scope).ConfigureAwait(false);
                return;
            }

            var resolution = await LocalEventContextBridge
                .ResolveAsync(scope.ServiceProvider, binding, workItem.ContextSnapshot, stoppingToken)
                .ConfigureAwait(false);
            contextFrame = LocalEventContextBridge.Activate(scope.ServiceProvider, resolution);

            var admission = await LocalEventAdmission
                .AdmitAsync(scope.ServiceProvider, binding, payload, LocalEventExecutionSource.ChannelConsume,
                    workItem.ContextSnapshot, workItem.AcceptedIdentity.DescriptorId, stoppingToken)
                .ConfigureAwait(false);
            if (!admission.Allowed)
            {
                throw new LocalEventAdmissionException(admission.ProblemCode!, admission.Problem!);
            }

            await LocalEventExecution.ExecuteAsync(scope.ServiceProvider, binding, payload, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            dispatchFailure = failure;
        }

        // Release the business scope first, then the context frames; both are independent facts.
        var scopeReleaseFailure = await TryReleaseScopeAsync(scope).ConfigureAwait(false);
        var frameReleaseFailure = contextFrame.DisposeAndCapture();
        if (scopeReleaseFailure is not null || frameReleaseFailure is not null)
        {
            _logger?.LogError(
                scopeReleaseFailure ?? frameReleaseFailure,
                "Releasing the execution environment for local event of type {EventType} failed (dispatch outcome: {Outcome}).",
                payload.GetType().Name,
                dispatchFailure is null
                    ? "handlers completed successfully"
                    : dispatchFailure is OperationCanceledException && stoppingToken.IsCancellationRequested
                        ? "dispatch was canceled during shutdown"
                        : "handlers reported a business failure");
        }

        if (dispatchFailure is null)
        {
            return;
        }

        if (dispatchFailure is OperationCanceledException && stoppingToken.IsCancellationRequested)
        {
            // Shutdown: do not record a dead letter for a canceled dispatch.
            return;
        }

        _logger?.LogError(dispatchFailure, "Error processing local event of type {EventType}", payload.GetType().Name);

        if (_recorder is null || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (!_registry.TryGet(payload.GetType(), out var failedBinding))
        {
            return;
        }

        try
        {
            var outcome = await _recorder
                .TryRecordAsync(failedBinding, payload, dispatchFailure, workItem.ContextSnapshot,
                    workItem.AcceptedIdentity, stoppingToken)
                .ConfigureAwait(false);

            if (outcome.Recorded)
            {
                _logger?.LogInformation(
                    "Recorded failed channel local event '{EventName}' to the dead-letter store.",
                    failedBinding.LocalName);
            }
            else if (outcome.Problem is not null)
            {
                _logger?.LogWarning(
                    "Channel local event '{EventName}' failed and was not recorded to the dead-letter store: {Problem}",
                    failedBinding.LocalName, outcome.Problem);
            }
        }
        catch (Exception recordFailure)
        {
            _logger?.LogError(recordFailure, "Failed to record event to dead letter store");
        }
    }

    private static async Task<Exception?> TryReleaseScopeAsync(AsyncServiceScope scope)
    {
        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception releaseFailure)
        {
            return releaseFailure;
        }
    }
}
