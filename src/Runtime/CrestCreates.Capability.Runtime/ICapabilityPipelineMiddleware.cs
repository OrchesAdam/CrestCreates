using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public delegate Task<CapabilityExecutionResult> CapabilityPipelineDelegate(CapabilityExecutionContext context);

public interface ICapabilityPipelineMiddleware
{
    Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next);
}
