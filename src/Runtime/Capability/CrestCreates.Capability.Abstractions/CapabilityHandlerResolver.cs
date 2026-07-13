using System.Collections.Concurrent;

namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityHandlerResolver : ICapabilityHandlerResolver
{
    private readonly ConcurrentDictionary<string, ICapabilityHandlerInvoker> _invokers = new();

    public void Register(string capabilityId, ICapabilityHandlerInvoker invoker)
    {
        if (!_invokers.TryAdd(capabilityId, invoker))
            throw new InvalidOperationException(
                $"Duplicate handler registration for capability '{capabilityId}'. " +
                "Each capability must have exactly one handler invoker.");
    }

    public ICapabilityHandlerInvoker? Resolve(string capabilityId)
    {
        _invokers.TryGetValue(capabilityId, out var invoker);
        return invoker;
    }

    /// <summary>
    /// Clears all registered invokers. Internal for test isolation.
    /// </summary>
    internal void Reset()
    {
        _invokers.Clear();
    }
}
