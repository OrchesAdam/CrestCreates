using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityHandlerResolver : ICapabilityHandlerResolver
{
    private readonly ConcurrentDictionary<string, ICapabilityHandlerInvoker> _invokers = new();

    public void Register(string capabilityId, ICapabilityHandlerInvoker invoker)
    {
        _invokers[capabilityId] = invoker;
    }

    public ICapabilityHandlerInvoker? Resolve(string capabilityId)
    {
        _invokers.TryGetValue(capabilityId, out var invoker);
        return invoker;
    }
}
