namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Optional extension point for generated or handwritten capability handlers
/// that need access to the execution context without bypassing the pipeline.
/// </summary>
public interface ICapabilityContextAwareHandlerInvoker : ICapabilityHandlerInvoker
{
    Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct);
}

