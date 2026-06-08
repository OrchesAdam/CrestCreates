using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormDescriptorTests
{
    [Fact]
    public void FormDescriptor_Kind_Is_Form()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2)
        };

        form.Kind.Should().Be(DescriptorKind.Form);
    }

    [Fact]
    public void FormDescriptor_References_Schema_By_VersionedRef()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3)
        };

        form.Schema.Id.Should().Be("schema_01");
        form.Schema.Version.Should().Be(3);
    }

    [Fact]
    public void FormDescriptor_Fields_Contain_UI_Metadata()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "CustomerCreateForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Fields = new[]
            {
                new FormFieldDescriptor
                {
                    SchemaFieldName = "Name",
                    Label = "Full Name",
                    Placeholder = "Enter your name",
                    Order = 0,
                    IsReadOnly = false
                },
                new FormFieldDescriptor
                {
                    SchemaFieldName = "Email",
                    Label = "Email Address",
                    Order = 1
                }
            }
        };

        form.Fields.Should().HaveCount(2);
        form.Fields[0].Label.Should().Be("Full Name");
        form.Fields[1].SchemaFieldName.Should().Be("Email");
    }

    [Fact]
    public void FormDescriptor_Defaults_Fields_To_Empty()
    {
        var form = new FormDescriptor
        {
            Id = "form_01",
            Name = "MinimalForm",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        form.Fields.Should().BeEmpty();
    }

    [Fact]
    public void FormFieldDescriptor_VisibilityCondition_Is_Optional()
    {
        var field = new FormFieldDescriptor
        {
            SchemaFieldName = "ApprovalNotes",
            VisibilityCondition = "Role == 'Manager'"
        };

        field.VisibilityCondition.Should().Be("Role == 'Manager'");
    }
}
