using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

/// <summary>
/// Publishes capability lifecycle events after handler execution.
/// On success: publishes "capability.succeeded" with status/output metadata.
/// On failure: publishes "capability.failed" with error metadata.
/// Passes through silently if no IEventPublisher is registered.
/// </summary>
public sealed class EventPublishingMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IEventPublisher? _publisher;

    public EventPublishingMiddleware(IEventPublisher? publisher = null)
    {
        _publisher = publisher;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var result = await next(context).ConfigureAwait(false);

        if (_publisher == null) return result;

        var eventName = result.IsSuccess
            ? "capability.succeeded"
            : "capability.failed";

        await _publisher.PublishAsync(eventName, new
        {
            capabilityName = context.CapabilityName,
            capabilityVersion = context.CapabilityVersion,
            correlationId = context.CorrelationId,
            status = result.Status.ToString(),
            errorCode = result.ErrorCode,
            durationMs = result.Duration.TotalMilliseconds
        }, context.CancellationToken).ConfigureAwait(false);

        return result;
    }
}
