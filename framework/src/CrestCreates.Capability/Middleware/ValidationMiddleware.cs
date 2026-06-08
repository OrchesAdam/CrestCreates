using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class ValidationMiddleware : ICapabilityPipelineMiddleware
{
    public Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        return next(context);
    }
}
