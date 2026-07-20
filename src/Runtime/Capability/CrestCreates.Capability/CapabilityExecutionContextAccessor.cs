using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

internal sealed class CapabilityExecutionContextAccessor : ICapabilityExecutionContextAccessor
{
    public CapabilityExecutionContext? Current { get; private set; }

    public void Set(CapabilityExecutionContext context) => Current = context;

    public void Clear(CapabilityExecutionContext context)
    {
        if (ReferenceEquals(Current, context))
            Current = null;
    }
}
