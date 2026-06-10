using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Facade layer over ICapabilityPipeline. The unified entry point for all capability execution.
/// </summary>
public interface ICapabilityDispatcher
{
    Task<CapabilityExecutionResult> DispatchAsync(
        IVersionedDescriptor descriptor,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);

    Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityId,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
