using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormRegistryTests
{
    private static FormDescriptor CreateForm(string id, string name, int version)
    {
        return new FormDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };
    }

    private class TestFormProvider : IDescriptorProvider<FormDescriptor>
    {
        private readonly List<FormDescriptor> _descriptors;
        public TestFormProvider(List<FormDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<FormDescriptor> GetDescriptors() => _descriptors;
    }

    private static FormRegistry CreateRegistry(params FormDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<FormDescriptor>(Array.Empty<IRegistryValidator<FormDescriptor>>());
        var registry = new FormRegistry(engine);
        registry.Build([new TestFormProvider(descriptors.ToList())]);
        return registry;
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = CreateRegistry(CreateForm("form_01", "CustomerCreateForm", 1));

        var result = registry.GetById("form_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerCreateForm");
    }

    [Fact]
    public void Multiple_Versions_Same_Name()
    {
        var registry = CreateRegistry(
            CreateForm("f1", "CustomerForm", 1),
            CreateForm("f2", "CustomerForm", 2));

        var all = registry.GetAllByName("CustomerForm");
        all.Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_Returns_All_Forms()
    {
        var registry = CreateRegistry(
            CreateForm("f1", "FormA", 1),
            CreateForm("f2", "FormB", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }

    [Fact]
    public void Build_Sets_State_To_Built()
    {
        var engine = new RegistryValidationEngine<FormDescriptor>(Array.Empty<IRegistryValidator<FormDescriptor>>());
        var registry = new FormRegistry(engine);
        var provider = new TestFormProvider([CreateForm("f1", "F", 1)]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Build_Runs_FormValidator()
    {
        var engine = new RegistryValidationEngine<FormDescriptor>(
            [new FormDescriptorValidator()]);
        var registry = new FormRegistry(engine);
        var validForm = CreateForm("f1", "ValidForm", 1);
        var provider = new TestFormProvider([validForm]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Build_Fails_On_ValidationError()
    {
        var engine = new RegistryValidationEngine<FormDescriptor>(
            [new FormDescriptorValidator()]);
        var registry = new FormRegistry(engine);
        var invalidForm = CreateForm("", "NoIdForm", 0);
        var provider = new TestFormProvider([invalidForm]);

        var act = () => registry.Build([provider]);

        act.Should().Throw<RegistryValidationException>();
        registry.State.Should().Be(RegistryState.Failed);
    }

    [Fact]
    public void FormSchemaBindingValidator_With_Real_Registries()
    {
        var schemaEngine = new RegistryValidationEngine<SchemaDescriptor>(
            Array.Empty<IRegistryValidator<SchemaDescriptor>>());
        var schemaRegistry = new SchemaRegistry(schemaEngine);
        var schemaProvider = new TestSchemaProviderForRegistry([
            new SchemaDescriptor
            {
                Id = "s1", Name = "CustomerSchema", Version = 1,
                Fields = new List<SchemaFieldDescriptor>
                {
                    new() { Name = "Name", FieldType = "string", IsRequired = true },
                    new() { Name = "Email", FieldType = "string", IsRequired = false }
                }
            }
        ]);
        schemaRegistry.Build([schemaProvider]);

        var formEngine = new RegistryValidationEngine<FormDescriptor>(
            [new FormDescriptorValidator()]);
        var formRegistry = new FormRegistry(formEngine);
        var formProvider = new TestFormProvider([
            new FormDescriptor
            {
                Id = "f1", Name = "CustomerForm", Version = 1,
                Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
                Fields = new List<FormFieldDescriptor>
                {
                    new() { SchemaFieldName = "Name" },
                    new() { SchemaFieldName = "Email" }
                }
            }
        ]);
        formRegistry.Build([formProvider]);

        var bindingValidator = new FormSchemaBindingValidator();
        var report = bindingValidator.Validate(formRegistry.GetAll(), schemaRegistry);

        report.HasErrors.Should().BeFalse();
        report.HasWarnings.Should().BeFalse();
    }

    private class TestSchemaProviderForRegistry : IDescriptorProvider<SchemaDescriptor>
    {
        private readonly List<SchemaDescriptor> _descriptors;
        public TestSchemaProviderForRegistry(List<SchemaDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
    }
}
