using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability;

public static class SystemEventDescriptors
{
    public static readonly EventDescriptor CapabilityExecuting = new()
    {
        Id = "evt_sys_capability_executing",
        Name = "capability.executing",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.StateTransition,
        Importance = EventImportance.Operational,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static readonly EventDescriptor CapabilitySucceeded = new()
    {
        Id = "evt_sys_capability_succeeded",
        Name = "capability.succeeded",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.Fact,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static readonly EventDescriptor CapabilityFailed = new()
    {
        Id = "evt_sys_capability_failed",
        Name = "capability.failed",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.Fact,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static readonly EventDescriptor CapabilityCompensated = new()
    {
        Id = "evt_sys_capability_compensated",
        Name = "capability.compensated",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.StateTransition,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static void RegisterAll(Event.EventRegistry registry)
    {
        registry.Register(CapabilityExecuting);
        registry.Register(CapabilitySucceeded);
        registry.Register(CapabilityFailed);
        registry.Register(CapabilityCompensated);
    }
}
