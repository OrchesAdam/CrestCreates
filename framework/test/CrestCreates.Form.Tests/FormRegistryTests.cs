using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
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

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = new FormRegistry();
        var form = CreateForm("form_01", "CustomerCreateForm", 1);
        registry.Register(form);

        var result = registry.GetById("form_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerCreateForm");
    }

    [Fact]
    public void Multiple_Versions_Same_Name()
    {
        var registry = new FormRegistry();
        registry.Register(CreateForm("f1", "CustomerForm", 1));
        registry.Register(CreateForm("f2", "CustomerForm", 2));

        var all = registry.GetAllByName("CustomerForm");
        all.Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_Returns_All_Forms()
    {
        var registry = new FormRegistry();
        registry.Register(CreateForm("f1", "FormA", 1));
        registry.Register(CreateForm("f2", "FormB", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }
}
