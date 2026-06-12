using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
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

    [Fact]
    public void FormFieldDescriptor_Defaults_Metadata_To_Empty()
    {
        var field = new FormFieldDescriptor
        {
            SchemaFieldName = "Name"
        };

        field.Metadata.Should().NotBeNull();
        field.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void FormFieldDescriptor_Allows_Control_Metadata_Without_Runtime_Behavior()
    {
        var field = new FormFieldDescriptor
        {
            SchemaFieldName = "Email",
            ControlType = "email",
            IsRequiredOverride = true,
            ValidationMessage = "Please enter a valid email",
            DefaultValueExpression = "\"user@example.com\"",
            OptionsSource = "static:domains",
            Metadata = new Dictionary<string, string>
            {
                ["minWidth"] = "200px",
                ["maxWidth"] = "400px"
            }
        };

        field.ControlType.Should().Be("email");
        field.IsRequiredOverride.Should().BeTrue();
        field.ValidationMessage.Should().Be("Please enter a valid email");
        field.DefaultValueExpression.Should().Be("\"user@example.com\"");
        field.OptionsSource.Should().Be("static:domains");
        field.Metadata["minWidth"].Should().Be("200px");
        field.Metadata["maxWidth"].Should().Be("400px");
    }

    [Fact]
    public void Metadata_IsExcluded_From_ContractHash()
    {
        var form1 = new FormDescriptor
        {
            Id = "f1", Name = "TestForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new FormFieldDescriptor
                {
                    SchemaFieldName = "Name",
                    Metadata = new Dictionary<string, string> { ["A"] = "1" }
                }
            }
        };
        var form2 = new FormDescriptor
        {
            Id = "f1", Name = "TestForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new FormFieldDescriptor
                {
                    SchemaFieldName = "Name",
                    Metadata = new Dictionary<string, string> { ["B"] = "2" }
                }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

        hash1.Should().Be(hash2);
    }
}
