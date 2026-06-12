using CrestCreates.Capability;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityRelationshipExtractorTests
{
    private readonly CapabilityRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Full_Capability_Returns_All_Relationships()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "approve-order",
            Name = "Approved Order",
            Version = 1,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-input", Version = 1 },
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-output", Version = 1 },
            Produces = new[] { new EventRef("event", "order.approved", 1) },
            Consumes = new[] { new EventRef("event", "order.submitted", 1) },
            SupersededById = "approve-order-v2"
        };

        var relationships = _extractor.Extract(capability);

        relationships.Should().HaveCount(5);
        relationships.Should().AllSatisfy(r => r.From.Version.Should().Be(capability.Version));
    }

    [Fact]
    public void Extract_Schema_Refs_Use_Correct_Schema_Namespace()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "test-input", Version = 1 }
        };

        var relationships = _extractor.Extract(capability);

        var schemaRel = relationships.Should().ContainSingle(r => r.Role == "InputSchema").Subject;
        schemaRel.To.Namespace.Should().Be("schema");
        schemaRel.To.Id.Should().Be("test-input");
    }

    [Fact]
    public void Extract_Nullable_InputSchema_Omitted()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            InputSchema = null,
            OutputSchema = null
        };

        var relationships = _extractor.Extract(capability);

        relationships.Should().BeEmpty();
    }

    [Fact]
    public void Extract_Event_Produces_Weak_Strength()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            Produces = new[] { new EventRef("event", "test.event", 1) }
        };

        var relationships = _extractor.Extract(capability);

        var eventRel = relationships.Should().ContainSingle(r => r.Kind == RelationshipKind.Produces && r.SourcePath == "Produces").Subject;
        eventRel.Strength.Should().Be(RelationshipStrength.Weak);
    }
}
