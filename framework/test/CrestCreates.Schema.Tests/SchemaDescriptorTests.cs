using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public class SchemaDescriptorTests
{
    [Fact]
    public void SchemaDescriptor_Implements_IVersionedDescriptor()
    {
        var descriptor = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor
                {
                    Name = "Name",
                    FieldType = "string",
                    IsRequired = true
                }
            }
        };

        descriptor.Should().BeAssignableTo<IVersionedDescriptor>();
        descriptor.Kind.Should().Be(DescriptorKind.Schema);
        descriptor.Version.Should().Be(1);
    }

    [Fact]
    public void SchemaDescriptor_Defaults_State_To_Active()
    {
        var descriptor = new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "Test",
            Version = 1
        };

        descriptor.State.Should().Be(DescriptorState.Active);
    }

    [Fact]
    public void SchemaFieldDescriptor_Records_All_Properties()
    {
        var field = new SchemaFieldDescriptor
        {
            Name = "Email",
            FieldType = "string",
            IsRequired = true,
            IsNullable = false,
            MaxLength = 200,
            Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
        };

        field.Name.Should().Be("Email");
        field.FieldType.Should().Be("string");
        field.IsRequired.Should().BeTrue();
        field.MaxLength.Should().Be(200);
        field.Pattern.Should().NotBeNull();
    }
}
