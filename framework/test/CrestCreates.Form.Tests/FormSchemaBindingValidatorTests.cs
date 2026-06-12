using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormSchemaBindingValidatorTests
{
    private readonly FormSchemaBindingValidator _validator = new();

    private static SchemaRegistry CreateSchemaRegistry(params SchemaDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<SchemaDescriptor>(
            Array.Empty<IRegistryValidator<SchemaDescriptor>>());
        var registry = new SchemaRegistry(engine);
        registry.Build([new TestSchemaProvider(descriptors.ToList())]);
        return registry;
    }

    private class TestSchemaProvider : IDescriptorProvider<SchemaDescriptor>
    {
        private readonly List<SchemaDescriptor> _descriptors;
        public TestSchemaProvider(List<SchemaDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
    }

    private static SchemaDescriptor CreateSchema(
        string id, string name, int version,
        params (string name, bool isRequired)[] fields)
    {
        return new SchemaDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Fields = fields.Select(f => new SchemaFieldDescriptor
            {
                Name = f.name,
                FieldType = "string",
                IsRequired = f.isRequired
            }).ToList()
        };
    }

    private static FormDescriptor CreateForm(
        string id, string name, int version,
        string schemaId, int schemaVersion,
        params string[] schemaFieldNames)
    {
        return new FormDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>(schemaId, schemaVersion),
            Fields = schemaFieldNames.Select(fn => new FormFieldDescriptor
            {
                SchemaFieldName = fn
            }).ToList()
        };
    }

    [Fact]
    public void Passes_When_AllFieldsExistInSchema()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true), ("Email", false)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Email");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Fails_When_FormFieldMissingInSchema()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Phone");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Severity == ValidationSeverity.Error
            && i.Message.Contains("Phone"));
    }

    [Fact]
    public void Fails_When_SchemaRefMissing()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s2", 1, "Name");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("not found"));
    }

    [Fact]
    public void Fails_When_SchemaVersionNotFound()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 99, "Name");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("v99") &&
            i.Message.Contains("v1"));
    }

    [Fact]
    public void Warns_When_RequiredSchemaFieldNotInForm()
    {
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true), ("InternalId", true)));
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeFalse();
        report.HasWarnings.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Severity == ValidationSeverity.Warning
            && i.Message.Contains("InternalId"));
    }

    [Fact]
    public void Uses_VersionedSchemaRef()
    {
        // Schema v1 has [Name, Email]; Schema v2 adds [Phone] and removes [Email]
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true), ("Email", false)),
            CreateSchema("s1", "CustomerSchema", 2, ("Name", true), ("Phone", false)));

        // Form requests v1 — Email should be valid
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Email");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Uses_GetByVersion_Not_GetById()
    {
        // Schema v1 has [Name]; Schema v2 (latest via GetById) adds [Phone]
        var schemaRegistry = CreateSchemaRegistry(
            CreateSchema("s1", "CustomerSchema", 1, ("Name", true)),
            CreateSchema("s1", "CustomerSchema", 2, ("Name", true), ("Phone", false)));

        // Form requests v1 — "Phone" should fail because v1 doesn't have it
        var form = CreateForm("f1", "CustomerForm", 1, "s1", 1, "Name", "Phone");

        var report = _validator.Validate([form], schemaRegistry);

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Phone") &&
            i.Message.Contains("v1"));
    }
}
