using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormRelationshipExtractorTests
{
    private readonly FormRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_Schema_Relationship()
    {
        var form = new FormDescriptor
        {
            Id = "order-form",
            Name = "Order Form",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-schema", Version = 2 }
        };

        var relationships = _extractor.Extract(form);

        relationships.Should().HaveCount(1);
        var rel = relationships[0];
        rel.From.Namespace.Should().Be("form");
        rel.From.Id.Should().Be("order-form");
        rel.From.Version.Should().Be(form.Version);
        rel.To.Namespace.Should().Be("schema");
        rel.To.Id.Should().Be("order-schema");
        rel.Kind.Should().Be(RelationshipKind.Uses);
        rel.Role.Should().Be("Schema");
        rel.SourcePath.Should().Be("Schema");
        rel.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Extract_Emits_Even_When_Schema_Id_Empty()
    {
        var form = new FormDescriptor
        {
            Id = "order-form",
            Name = "Order Form",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "", Version = 0 }
        };

        var relationships = _extractor.Extract(form);

        relationships.Should().HaveCount(1);
        relationships[0].To.Id.Should().Be("");
    }

    [Fact]
    public void SupportedKind_Is_Form()
    {
        _extractor.SupportedKind.Should().Be(DescriptorKind.Form);
    }

    [Fact]
    public void FormExtractor_Creates_UsesRelationship_ToSchema()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2)
        };

        var relationships = _extractor.Extract(form);

        relationships.Should().HaveCount(1);
        relationships[0].From.Id.Should().Be("form_01");
        relationships[0].To.Id.Should().Be("schema_01");
        relationships[0].Kind.Should().Be(RelationshipKind.Uses);
    }

    [Fact]
    public void Form_DoesNot_Depend_On_HumanTask()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        var relationships = _extractor.Extract(form);

        relationships.Should().OnlyContain(r => r.Kind == RelationshipKind.Uses);
    }
}
