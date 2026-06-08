using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

/// <summary>
/// Validates capability input against the InputSchema declared on the CapabilityDescriptor.
/// Currently a pass-through placeholder — schema validation is deferred until the Schema
/// validation engine is implemented (see spec §8, pipeline step 2).
/// Once implemented, this middleware will resolve the SchemaDescriptor from the registry,
/// validate the input payload, and return CAPABILITY_VALIDATION_FAILED on mismatch.
/// </summary>
public sealed class ValidationMiddleware : ICapabilityPipelineMiddleware
{
    public Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        return next(context);
    }
}
