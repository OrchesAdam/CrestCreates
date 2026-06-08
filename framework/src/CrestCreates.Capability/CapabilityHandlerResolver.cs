using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityHandlerResolver : ICapabilityHandlerResolver
{
    private readonly ConcurrentDictionary<string, ICapabilityHandlerInvoker> _invokers = new();

    public void Register(string capabilityName, ICapabilityHandlerInvoker invoker)
    {
        _invokers[capabilityName] = invoker;
    }

    public ICapabilityHandlerInvoker? Resolve(string capabilityName)
    {
        _invokers.TryGetValue(capabilityName, out var invoker);
        return invoker;
    }
}
