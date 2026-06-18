using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class MetricsMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IPipelineMetrics? _metrics;

    public MetricsMiddleware(IPipelineMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var result = await next(context).ConfigureAwait(false);

        _metrics?.RecordExecution(
            context.CapabilityName,
            result.IsSuccess,
            DateTimeOffset.UtcNow - startedAt);

        return result;
    }
}