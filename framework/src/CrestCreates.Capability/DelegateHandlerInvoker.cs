using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

/// <summary>
/// AOT-safe handler invoker backed by a delegate. Enables reflection-free handler
/// registration. Source generators will emit these wrappers for each handler type.
/// </summary>
public sealed class DelegateHandlerInvoker : ICapabilityHandlerInvoker
{
    private readonly Func<object?, CancellationToken, Task<object?>> _invoke;

    public DelegateHandlerInvoker(Func<object?, CancellationToken, Task<object?>> invoke)
    {
        _invoke = invoke;
    }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => _invoke(input, ct);
}
