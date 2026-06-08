namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityHandlerInvoker
{
    Task<object?> InvokeAsync(object? input, CancellationToken ct);
}
