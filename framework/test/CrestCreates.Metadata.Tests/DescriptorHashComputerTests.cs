using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
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
    public void Capability_ContractHash_Is_Stable()
    {
        var cap1 = new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            Permissions = new[] { "customer.create" }
        };
        var cap2 = new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            Permissions = new[] { "customer.create" }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(cap1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(cap2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void EventDescriptor_Same_Content_Produces_Same_ContractHash()
    {
        var evt1 = new Event.Abstractions.EventDescriptor
        {
            Id = "evt_01", Name = "crm.customer.created", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = Event.Abstractions.EventCategory.Domain,
            Semantic = Event.Abstractions.EventSemantic.Fact,
            Importance = Event.Abstractions.EventImportance.Critical
        };
        var evt2 = new Event.Abstractions.EventDescriptor
        {
            Id = "evt_01", Name = "crm.customer.created", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Category = Event.Abstractions.EventCategory.Domain,
            Semantic = Event.Abstractions.EventSemantic.Fact,
            Importance = Event.Abstractions.EventImportance.Critical
        };

        var h1 = DescriptorHashComputer.ComputeContractHash(evt1);
        var h2 = DescriptorHashComputer.ComputeContractHash(evt2);

        h1.Should().Be(h2);
    }

    [Fact]
    public void WorkflowStep_ContractHash_Includes_Step_Id_Not_Name()
    {
        var wf1 = new Workflow.Abstractions.WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new Workflow.Abstractions.WorkflowStep
                {
                    Id = "step_01", Name = "Step A",
                    Target = new Workflow.Abstractions.CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    }
                }
            }
        };
        var wf2 = new Workflow.Abstractions.WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new Workflow.Abstractions.WorkflowStep
                {
                    Id = "step_01", Name = "Renamed Step",
                    Target = new Workflow.Abstractions.CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    }
                }
            }
        };

        var h1 = DescriptorHashComputer.ComputeContractHash(wf1);
        var h2 = DescriptorHashComputer.ComputeContractHash(wf2);

        h1.Should().Be(h2);
    }

    [Fact]
    public void FormDescriptor_ContractHash_Excludes_UI_Cosmetic_Fields()
    {
        var form1 = new Form.Abstractions.FormDescriptor
        {
            Id = "form_01", Name = "CustomerForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Email", Label = "Email", Order = 0 }
            }
        };
        var form2 = new Form.Abstractions.FormDescriptor
        {
            Id = "form_01", Name = "CustomerForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Email", Label = "Email Address", Order = 0 }
            }
        };

        var h1 = DescriptorHashComputer.ComputeContractHash(form1);
        var h2 = DescriptorHashComputer.ComputeContractHash(form2);

        // Label is cosmetic — same structural contract
        h1.Should().Be(h2);
    }

    [Fact]
    public void FormContractHash_Changes_When_ControlTypeChanges()
    {
        var form1 = new Form.Abstractions.FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Name", ControlType = "text" }
            }
        };
        var form2 = new Form.Abstractions.FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Name", ControlType = "select" }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void FormContractHash_Changes_When_IsRequiredOverrideChanges()
    {
        var form1 = new Form.Abstractions.FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Name", IsRequiredOverride = true }
            }
        };
        var form2 = new Form.Abstractions.FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Name", IsRequiredOverride = false }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void FormContractHash_DoesNotChange_When_ValidationMessageChanges()
    {
        var form1 = new Form.Abstractions.FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Name", ValidationMessage = "Msg A" }
            }
        };
        var form2 = new Form.Abstractions.FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[]
            {
                new Form.Abstractions.FormFieldDescriptor { SchemaFieldName = "Name", ValidationMessage = "Msg B" }
            }
        };

        var hash1 = DescriptorHashComputer.ComputeContractHash(form1);
        var hash2 = DescriptorHashComputer.ComputeContractHash(form2);

        hash1.Should().Be(hash2);
    }
}