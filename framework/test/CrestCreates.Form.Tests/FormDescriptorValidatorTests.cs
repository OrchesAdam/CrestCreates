using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormDescriptorValidatorTests
{
    private readonly FormDescriptorValidator _validator = new();

    private static FormDescriptor CreateValidForm(
        string id = "form_01",
        string name = "TestForm",
        int version = 1,
        string schemaId = "schema_01",
        int schemaVersion = 1,
        FormFieldDescriptor[]? fields = null)
    {
        return new FormDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>(schemaId, schemaVersion),
            Fields = fields ?? Array.Empty<FormFieldDescriptor>()
        };
    }

    private static FormFieldDescriptor CreateField(string schemaFieldName, int order = 0)
    {
        return new FormFieldDescriptor
        {
            SchemaFieldName = schemaFieldName,
            Order = order
        };
    }

    [Fact]
    public void Rejects_EmptyId()
    {
        var form = CreateValidForm(id: "");
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Severity == ValidationSeverity.Error
            && i.Message.Contains("Id must not be null or whitespace"));
    }

    [Fact]
    public void Rejects_EmptyName()
    {
        var form = CreateValidForm(name: "");
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Name must not be null or whitespace"));
    }

    [Fact]
    public void Rejects_NonPositiveVersion()
    {
        var form = CreateValidForm(version: 0);
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("positive"));
    }

    [Fact]
    public void Rejects_EmptySchemaRef()
    {
        var form = CreateValidForm(schemaId: "");
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Schema.Id"));
    }

    [Fact]
    public void Rejects_EmptySchemaFieldName()
    {
        var form = CreateValidForm(fields: new[]
        {
            new FormFieldDescriptor { SchemaFieldName = "" }
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("SchemaFieldName"));
    }

    [Fact]
    public void Rejects_DuplicateSchemaFieldName()
    {
        var form = CreateValidForm(fields: new[]
        {
            CreateField("Name", 0),
            CreateField("Name", 1)
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Duplicate SchemaFieldName"));
    }

    [Fact]
    public void Allows_PartialSchemaCoverage()
    {
        var form = CreateValidForm(fields: new[]
        {
            CreateField("Name", 0),
            CreateField("Email", 1)
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Allows_DuplicateOrder()
    {
        var form = CreateValidForm(fields: new[]
        {
            CreateField("Name", 0),
            CreateField("Email", 0)
        });
        var report = _validator.Validate([form]);
        report.HasErrors.Should().BeFalse();
    }
}
