using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorHashComputerTests
{
    [Fact]
    public void ComputeDefinitionHash_Same_Content_Produces_Same_Hash()
    {
        var schema1 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };
        var schema2 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeDefinitionHash(schema1);
        var hash2 = DescriptorHashComputer.ComputeDefinitionHash(schema2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_Different_Content_Produces_Different_Hash()
    {
        var schema1 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1
        };
        var schema2 = new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "CustomerInput",
            Version = 2
        };

        var hash1 = DescriptorHashComputer.ComputeDefinitionHash(schema1);
        var hash2 = DescriptorHashComputer.ComputeDefinitionHash(schema2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContractHash_Excludes_Cosmetic_Fields()
    {
        var schema = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor
                {
                    Name = "Email",
                    FieldType = "string",
                    IsRequired = true,
                    MaxLength = 200
                }
            }
        };

        var contractHash = DescriptorHashComputer.ComputeContractHash(schema);
        var definitionHash = DescriptorHashComputer.ComputeDefinitionHash(schema);

        contractHash.Should().NotBe(definitionHash);
    }

    [Fact]
    public void ContractHash_Ignores_Field_Declaration_Order()
    {
        var schema1 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "Test",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "A", FieldType = "string" },
                new SchemaFieldDescriptor { Name = "B", FieldType = "int" }
            }
        };
        var schema2 = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "Test",
            Version = 1,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "B", FieldType = "int" },
                new SchemaFieldDescriptor { Name = "A", FieldType = "string" }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(schema1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(schema2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Capability_ContractHash_Excludes_Aliases()
    {
        var cap1 = new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            Aliases = new List<string> { "crm.customer.register" }
        };
        var cap2 = new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            Aliases = new List<string> { "crm.customer.add" }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(cap1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(cap2);

        hash1.Should().Be(hash2);
    }
}