namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityHandlerResolver
{
    ICapabilityHandlerInvoker? Resolve(string capabilityId);
}
