using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class SchemaRelationshipExtractorTests
{
    private readonly SchemaRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_References_Relationships()
    {
        var schema = new SchemaDescriptor
        {
            Id = "order",
            Name = "Order",
            Version = 1,
            References = new[]
            {
                new VersionedDescriptorRef<SchemaDescriptor> { Id = "customer", Version = 2 },
                new VersionedDescriptorRef<SchemaDescriptor> { Id = "product", Version = 1 }
            }
        };

        var relationships = _extractor.Extract(schema);

        relationships.Should().HaveCount(2);
        relationships.Should().AllSatisfy(r =>
        {
            r.Kind.Should().Be(RelationshipKind.References);
            r.From.Namespace.Should().Be("schema");
            r.From.Id.Should().Be("order");
            r.From.Version.Should().Be(schema.Version);
            r.To.Namespace.Should().Be("schema");
            r.SourcePath.Should().Be("References");
            r.Strength.Should().Be(RelationshipStrength.Weak);
        });
        relationships[0].To.Id.Should().Be("customer");
        relationships[1].To.Id.Should().Be("product");
    }

    [Fact]
    public void Extract_Returns_Empty_When_No_References()
    {
        var schema = new SchemaDescriptor
        {
            Id = "order",
            Name = "Order",
            Version = 1,
            References = Array.Empty<VersionedDescriptorRef<SchemaDescriptor>>()
        };

        var relationships = _extractor.Extract(schema);

        relationships.Should().BeEmpty();
    }

    [Fact]
    public void SupportedKind_Is_Schema()
    {
        _extractor.SupportedKind.Should().Be(DescriptorKind.Schema);
    }

    [Fact]
    public void DescriptorType_Is_SchemaDescriptor()
    {
        _extractor.DescriptorType.Should().Be(typeof(SchemaDescriptor));
    }
}
