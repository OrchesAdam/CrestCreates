namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityPipeline
{
    Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
