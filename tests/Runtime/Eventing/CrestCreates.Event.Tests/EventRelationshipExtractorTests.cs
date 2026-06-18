using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventRelationshipExtractorTests
{
    private readonly EventRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_PayloadSchemaRef_Relationship()
    {
        var descriptor = CreateEventDescriptor("order-approved", "order-schema", 2);

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(1);
        var rel = relationships[0];
        rel.From.Namespace.Should().Be("event");
        rel.From.Id.Should().Be("order-approved");
        rel.From.Version.Should().Be(descriptor.Version);
        rel.To.Namespace.Should().Be("schema");
        rel.To.Id.Should().Be("order-schema");
        rel.Kind.Should().Be(RelationshipKind.Uses);
        rel.Role.Should().Be("PayloadSchema");
        rel.SourcePath.Should().Be("PayloadSchemaRef");
        rel.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Extract_Emits_Even_When_PayloadSchemaRef_Id_Empty()
    {
        var descriptor = CreateEventDescriptor("order-approved", "", 0);

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(1);
        relationships[0].To.Id.Should().Be("");
    }

    [Fact]
    public void SupportedKind_Is_Event()
    {
        _extractor.SupportedKind.Should().Be(DescriptorKind.Event);
    }

    [Fact]
    public void DescriptorType_Is_GeneratedEventDescriptor()
    {
        _extractor.DescriptorType.Should().Be(typeof(GeneratedEventDescriptor));
    }

    private static GeneratedEventDescriptor CreateEventDescriptor(string id, string schemaId, int schemaVersion)
    {
        return new GeneratedEventDescriptor
        {
            Id = id,
            Name = "Test Event",
            Version = 1,
            PayloadType = typeof(string),
            PayloadSchemaRef = new VersionedDescriptorRef<SchemaDescriptor>
            {
                Id = schemaId,
                Version = schemaVersion
            }
        };
    }
}
