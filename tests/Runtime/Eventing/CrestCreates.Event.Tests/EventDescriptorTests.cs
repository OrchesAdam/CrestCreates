using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventDescriptorTests
{
    [Fact]
    public void EventDescriptor_Kind_Is_Event()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = EventCategory.Domain,
            Semantic = EventSemantic.Fact,
            Importance = EventImportance.Critical
        };

        evt.Kind.Should().Be(DescriptorKind.Event);
    }

    [Fact]
    public void EventDescriptor_Implements_IVersionedDescriptor()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 3
        };

        IVersionedDescriptor vd = evt;
        vd.Version.Should().Be(3);
    }

    [Fact]
    public void EventDescriptor_Defaults_State_To_Active()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        evt.State.Should().Be(DescriptorState.Active);
    }

    [Fact]
    public void EventDescriptor_Classification_Is_Preserved()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = EventCategory.Domain,
            Semantic = EventSemantic.StateTransition,
            Importance = EventImportance.Business
        };

        evt.Category.Should().Be(EventCategory.Domain);
        evt.Semantic.Should().Be(EventSemantic.StateTransition);
        evt.Importance.Should().Be(EventImportance.Business);
    }

    [Fact]
    public void EventDescriptor_ChangeKind_Is_Declared()
    {
        var evt = new EventDescriptor
        {
            Id = "evt_01",
            Name = "crm.customer.created",
            Version = 2,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2),
            ChangeKind = SchemaChangeKind.Additive
        };

        evt.ChangeKind.Should().Be(SchemaChangeKind.Additive);
    }
}
