using CrestCreates.Metadata;

namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityPipeline
{
    Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityIdOrName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);

    Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityDescriptor descriptor,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
