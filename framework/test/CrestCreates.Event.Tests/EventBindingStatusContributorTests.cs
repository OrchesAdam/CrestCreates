using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventBindingStatusContributorTests
{
    [Fact]
    public void Evaluate_Deprecated_ReturnsPartiallyBound()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Deprecated, PayloadType = typeof(string) };

        var result = contributor.Evaluate(evt);
        result.Status.Should().Be(DescriptorBindingStatus.PartiallyBound);
        result.Issues.Should().Contain(i => i.Code == "WARN_DEPRECATED");
    }

    [Fact]
    public void Evaluate_Removed_ReturnsUnsupported()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Removed, PayloadType = typeof(string) };

        var result = contributor.Evaluate(evt);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_REMOVED");
    }

    [Fact]
    public void Evaluate_MissingPayloadSchema_ReturnsInvalid()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion(It.IsAny<string>(), It.IsAny<int>())).Returns((SchemaDescriptor?)null);

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Active, PayloadType = typeof(string),
            PayloadSchemaRef = new VersionedDescriptorRef<SchemaDescriptor>("missing", 1) };

        var result = contributor.Evaluate(evt);
        result.Status.Should().Be(DescriptorBindingStatus.Invalid);
        result.Issues.Should().Contain(i => i.Code == "REF_MISSING_SCHEMA");
    }

    [Fact]
    public void Evaluate_Active_ReturnsRuntimeReady()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("test.schema", 1))
            .Returns(new SchemaDescriptor { Id = "test.schema", Name = "Schema", Version = 1 });

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Active, PayloadType = typeof(string),
            PayloadSchemaRef = new VersionedDescriptorRef<SchemaDescriptor>("test.schema", 1) };

        var result = contributor.Evaluate(evt);
        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
