using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormBindingStatusContributorTests
{
    private static FormDescriptor CreateForm(string id = "test.form", string schemaId = "test.schema", int schemaVersion = 1,
        params FormFieldDescriptor[] fields)
    {
        return new FormDescriptor
        {
            Id = id, Name = "Test Form", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>(schemaId, schemaVersion),
            Fields = fields.ToList()
        };
    }

    private static SchemaDescriptor CreateSchema(string id, int version, params SchemaFieldDescriptor[] fields)
    {
        return new SchemaDescriptor { Id = id, Name = "Test Schema", Version = version, Fields = fields.ToList() };
    }

    [Fact]
    public void Evaluate_MissingSchemaVersion_ReturnsInvalid()
    {
        var formRegistry = new Mock<IFormRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("missing", 1)).Returns((SchemaDescriptor?)null);

        var contributor = new FormBindingStatusContributor(formRegistry.Object, schemaRegistry.Object);
        var form = CreateForm(schemaId: "missing");

        var result = contributor.Evaluate(form);

        result.Status.Should().Be(DescriptorBindingStatus.Invalid);
        result.Issues.Should().Contain(i => i.Code == "REF_MISSING_SCHEMA_VERSION");
    }

    [Fact]
    public void Evaluate_RequiredSchemaFieldMissing_ReturnsPartiallyBound()
    {
        var formRegistry = new Mock<IFormRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("test.schema", 1))
            .Returns(CreateSchema("test.schema", 1,
                new SchemaFieldDescriptor { Name = "name", IsRequired = true }));

        var contributor = new FormBindingStatusContributor(formRegistry.Object, schemaRegistry.Object);
        var form = CreateForm(); // no fields

        var result = contributor.Evaluate(form);

        result.Status.Should().Be(DescriptorBindingStatus.PartiallyBound);
        result.Issues.Should().Contain(i => i.Code == "BIND_MISSING_REQUIRED_FIELD");
    }

    [Fact]
    public void Evaluate_ValidFormAndSchema_ReturnsRuntimeReady()
    {
        var formRegistry = new Mock<IFormRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("test.schema", 1))
            .Returns(CreateSchema("test.schema", 1,
                new SchemaFieldDescriptor { Name = "name" }));

        var contributor = new FormBindingStatusContributor(formRegistry.Object, schemaRegistry.Object);
        var form = CreateForm(fields: new FormFieldDescriptor { SchemaFieldName = "name" });

        var result = contributor.Evaluate(form);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
