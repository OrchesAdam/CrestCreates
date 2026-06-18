using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class RateLimitMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IRateLimitStore? _store;
    private readonly int _defaultMaxRequests;
    private readonly TimeSpan _defaultWindow;

    public RateLimitMiddleware(
        IRateLimitStore? store = null,
        int defaultMaxRequests = 100,
        TimeSpan? defaultWindow = null)
    {
        _store = store;
        _defaultMaxRequests = defaultMaxRequests;
        _defaultWindow = defaultWindow ?? TimeSpan.FromMinutes(1);
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_store == null)
            return await next(context).ConfigureAwait(false);

        var allowed = await _store.CheckRateLimitAsync(
            context.CapabilityName,
            _defaultMaxRequests,
            _defaultWindow,
            context.CancellationToken).ConfigureAwait(false);

        if (!allowed)
        {
            return CapabilityExecutionResult.Failure(
                "RATE_LIMIT_EXCEEDED",
                $"Rate limit exceeded for '{context.CapabilityName}'. Max {_defaultMaxRequests} per {_defaultWindow.TotalSeconds}s.",
                TimeSpan.Zero);
        }

        return await next(context).ConfigureAwait(false);
    }
}