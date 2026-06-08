using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventRegistryTests
{
    private static EventDescriptor CreateEvent(string id, string name, int version,
        EventCategory category = EventCategory.Domain,
        EventSemantic semantic = EventSemantic.Fact,
        EventImportance importance = EventImportance.Business)
    {
        return new EventDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = category,
            Semantic = semantic,
            Importance = importance
        };
    }

    [Fact]
    public void GetById_Returns_Correct_Event()
    {
        var registry = new EventRegistry();
        var evt = CreateEvent("evt_01", "crm.customer.created", 1);
        registry.Register(evt);

        var result = registry.GetById("evt_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("crm.customer.created");
    }

    [Fact]
    public void GetByCategory_Filters_Correctly()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.domain", 1, EventCategory.Domain));
        registry.Register(CreateEvent("e2", "evt.integration", 1, EventCategory.Integration));
        registry.Register(CreateEvent("e3", "evt.capability", 1, EventCategory.Capability));

        var domain = registry.GetByCategory(EventCategory.Domain);
        domain.Should().HaveCount(1);
        domain[0].Id.Should().Be("e1");
    }

    [Fact]
    public void GetBySemantic_Filters_Correctly()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.fact", 1, semantic: EventSemantic.Fact));
        registry.Register(CreateEvent("e2", "evt.transition", 1, semantic: EventSemantic.StateTransition));

        var facts = registry.GetBySemantic(EventSemantic.Fact);
        facts.Should().HaveCount(1);
    }

    [Fact]
    public void GetByImportance_Filters_Correctly()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.critical", 1, importance: EventImportance.Critical));
        registry.Register(CreateEvent("e2", "evt.ephemeral", 1, importance: EventImportance.Ephemeral));

        var critical = registry.GetByImportance(EventImportance.Critical);
        critical.Should().HaveCount(1);
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active_Version()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.test", 1));
        registry.Register(CreateEvent("e2", "evt.test", 2));
        registry.Register(new EventDescriptor
        {
            Id = "e3", Name = "evt.test", Version = 3, State = DescriptorState.Deprecated,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        });

        var active = registry.GetActiveVersion("evt.test");
        active.Should().NotBeNull();
        active!.Version.Should().Be(2);
    }

    [Fact]
    public void GetLatestVersion_Returns_Highest_Version_Regardless_Of_State()
    {
        var registry = new EventRegistry();
        registry.Register(CreateEvent("e1", "evt.test", 1));
        registry.Register(new EventDescriptor
        {
            Id = "e3", Name = "evt.test", Version = 5, State = DescriptorState.Deprecated,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        });

        var latest = registry.GetLatestVersion("evt.test");
        latest.Should().NotBeNull();
        latest!.Version.Should().Be(5);
    }
}
