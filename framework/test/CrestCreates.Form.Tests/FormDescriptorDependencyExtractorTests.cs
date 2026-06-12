using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormDescriptorDependencyExtractorTests
{
    [Fact]
    public void FormDependencyExtractor_Adds_UsesEdge_ToSchema()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2)
        };

        var edges = FormDescriptorDependencyExtractor.Extract(form);

        edges.Should().HaveCount(1);
        edges[0].SourceId.Should().Be("form_01");
        edges[0].TargetId.Should().Be("schema_01");
        edges[0].Kind.Should().Be(DescriptorDependencyKind.Uses);
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

        var edges = FormDescriptorDependencyExtractor.Extract(form);

        edges.Should().OnlyContain(e => e.Kind == DescriptorDependencyKind.Uses);
    }
}
