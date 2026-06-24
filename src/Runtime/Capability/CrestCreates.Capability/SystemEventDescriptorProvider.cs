using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability;

public sealed class SystemEventDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() => [
        new GeneratedEventDescriptor
        {
            Id = "evt_sys_capability_executing",
            Name = "capability.executing",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(object),
            Scope = EventScope.Local,
            Reliability = EventReliability.AtLeastOnce,
            Importance = EventImportance.Operational,
            ChangeKind = Schema.Abstractions.SchemaChangeKind.Additive
        },
        new GeneratedEventDescriptor
        {
            Id = "evt_sys_capability_succeeded",
            Name = "capability.succeeded",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(object),
            Scope = EventScope.Integration,
            Reliability = EventReliability.AtLeastOnce,
            Importance = EventImportance.Business,
            ChangeKind = Schema.Abstractions.SchemaChangeKind.Additive
        },
        new GeneratedEventDescriptor
        {
            Id = "evt_sys_capability_failed",
            Name = "capability.failed",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(object),
            Scope = EventScope.Integration,
            Reliability = EventReliability.AtLeastOnce,
            Importance = EventImportance.Business,
            ChangeKind = Schema.Abstractions.SchemaChangeKind.Additive
        },
        new GeneratedEventDescriptor
        {
            Id = "evt_sys_capability_compensated",
            Name = "capability.compensated",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(object),
            Scope = EventScope.Integration,
            Reliability = EventReliability.AtLeastOnce,
            Importance = EventImportance.Business,
            ChangeKind = Schema.Abstractions.SchemaChangeKind.Additive
        }
    ];
}
