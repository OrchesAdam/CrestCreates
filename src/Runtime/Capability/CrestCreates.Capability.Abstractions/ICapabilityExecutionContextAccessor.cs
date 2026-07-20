namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Scoped access to the current pipeline context for generated handlers that
/// need trusted invocation metadata. It is populated only by the pipeline.
/// </summary>
public interface ICapabilityExecutionContextAccessor
{
    CapabilityExecutionContext? Current { get; }
}
