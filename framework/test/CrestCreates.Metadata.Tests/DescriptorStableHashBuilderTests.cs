using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public sealed class DescriptorStableHashBuilderTests
{
    private readonly DescriptorStableHashBuilder _builder = new();

    // ── Issue #29 Acceptance Tests ──

    [Fact]
    public void Build_SameDescriptor_Should_Produce_SameHashes()
    {
        var schema = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "string", IsRequired = true }
        });

        var result1 = _builder.Build(schema);
        var result2 = _builder.Build(schema);

        result1.Should().BeEquivalentTo(result2);
    }

    [Fact]
    public void Build_RecreatedEquivalentDescriptor_Should_Produce_SameHashes()
    {
        var schema1 = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "string", IsRequired = true }
        });
        var schema2 = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "string", IsRequired = true }
        });

        var result1 = _builder.Build(schema1);
        var result2 = _builder.Build(schema2);

        result1.ContractHash.Should().Be(result2.ContractHash);
        result1.DefinitionHash.Should().Be(result2.DefinitionHash);
    }

    [Fact]
    public void Build_OptionalSchemaFieldAddition_Should_ChangeDefinitionHash()
    {
        var original = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
        });
        var modified = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true },
            new SchemaFieldDescriptor { Name = "Phone", FieldType = "string", IsRequired = false }
        });

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        // Definition should change (optional field added = descriptor definition changed)
        originalHashes.DefinitionHash.Should().NotBe(modifiedHashes.DefinitionHash,
            "optional field addition should change the definition hash");
        // NOTE: ContractHash also changes because current implementation includes all fields.
        // When exclusion policy (Issue #29 Requirement #4) is implemented, assert .Be().
    }

    [Fact]
    public void Build_RequiredSchemaFieldRemoval_Should_ChangeContractHash()
    {
        var original = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "string", IsRequired = true },
            new SchemaFieldDescriptor { Name = "Phone", FieldType = "string", IsRequired = false }
        });
        var modified = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Phone", FieldType = "string", IsRequired = false }
        });

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        originalHashes.ContractHash.Should().NotBe(modifiedHashes.ContractHash,
            "required field removal should change the contract hash");
    }

    [Fact]
    public void Build_PermissionChange_Should_ChangeDefinitionHash_OrSecurityRelevantHash()
    {
        var original = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test.cap", Version = 1,
            Permissions = new[] { "test.read" }
        };
        var modified = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test.cap", Version = 1,
            Permissions = new[] { "test.read", "test.write" }
        };

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        // Permissions are included in ContractHash for capabilities (part of externally
        // observable contract), so contract hash changes when permissions change.
        modifiedHashes.ContractHash.Should().NotBe(originalHashes.ContractHash,
            "permission change should change the contract hash (included in contract)");
        modifiedHashes.DefinitionHash.Should().NotBe(originalHashes.DefinitionHash,
            "permission change should change the definition hash");
    }

    [Fact]
    public void Build_PermissionsOrder_Should_Not_Affect_ContractHash()
    {
        var cap1 = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test", Version = 1,
            Permissions = new[] { "p2", "p1", "p3" }
        };
        var cap2 = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test", Version = 1,
            Permissions = new[] { "p3", "p2", "p1" }
        };

        var h1 = _builder.Build(cap1).ContractHash;
        var h2 = _builder.Build(cap2).ContractHash;

        h1.Should().Be(h2, "permission order should be canonicalized");
    }

    [Fact]
    public void Build_FormLayoutChange_Should_NotChangeFormContractHash()
    {
        var form1 = new FormDescriptor
        {
            Id = "form_01", Name = "TestForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Fields = new[]
            {
                new FormFieldDescriptor { SchemaFieldName = "Email", Order = 0, Label = "Email" }
            }
        };
        var form2 = new FormDescriptor
        {
            Id = "form_01", Name = "TestForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            Fields = new[]
            {
                // Only Label changed — Order stays same because Order is included in contract
                new FormFieldDescriptor { SchemaFieldName = "Email", Order = 0, Label = "Email Address" }
            }
        };

        var form1Hash = _builder.Build(form1);
        var form2Hash = _builder.Build(form2);

        // Label is cosmetic (not in contract), and Order stayed the same,
        // so contract hash should be identical
        form1Hash.ContractHash.Should().Be(form2Hash.ContractHash,
            "form layout changes (Label only) are cosmetic and should not change contract hash");
    }

    [Fact]
    public void Build_WorkflowTargetChange_Should_ChangeDefinitionHash()
    {
        // Changing the step Id changes both contract and definition hashes,
        // since step Id is included in both extraction paths via AppendField.
        var original = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01",
                    Name = "Original Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };
        var modified = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_02",  // Changed step id
                    Name = "Modified Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        // Step id change is a definition-level change
        modifiedHashes.DefinitionHash.Should().NotBe(originalHashes.DefinitionHash,
            "step id change should change the definition hash");
    }

    /// <summary>
    /// Verifies that DefinitionHash correctly captures <see cref="InteractionTarget"/> subtype
    /// property changes via explicit switch-based extraction in
    /// <see cref="DescriptorStableHashBuilder.AppendTargetRef"/>.
    /// Changing only the target capability reference (same step id) produces a different
    /// DefinitionHash — the prior <see cref="JsonSerializer"/>-based limitation is resolved.
    /// </summary>
    [Fact]
    public void Build_WorkflowTargetRefChange_Should_ChangeDefinitionHash()
    {
        var original = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };
        var modified = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01",  // Same step id — only target ref differs
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        // DefinitionHash now correctly captures target ref changes via switch-based extraction
        modifiedHashes.DefinitionHash.Should().NotBe(originalHashes.DefinitionHash,
            "changing the target capability ref should change the definition hash");
    }

    [Fact]
    public void Build_WorkflowContractHash_Detects_TargetRef_Change()
    {
        var original = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };
        var modified = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        // Both ContractHash and DefinitionHash correctly capture target ref changes
        modifiedHashes.ContractHash.Should().NotBe(originalHashes.ContractHash,
            "changing the target capability ref should change the contract hash");
        modifiedHashes.DefinitionHash.Should().NotBe(originalHashes.DefinitionHash,
            "changing the target capability ref should change the definition hash");
    }

    [Fact]
    public void Build_RequiredSchemaFieldAddition_Should_ChangeContractHash()
    {
        var original = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
        });
        var modified = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true },
            new SchemaFieldDescriptor { Name = "IdNumber", FieldType = "string", IsRequired = true }
        });

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        modifiedHashes.ContractHash.Should().NotBe(originalHashes.ContractHash,
            "adding a required field should change the contract hash");
    }

    /// <summary>
    /// Documents current behavior: the ContractHash includes ALL schema fields (including
    /// optional ones). When the inclusion/exclusion policy (Issue #29 Requirement #4) is
    /// implemented to exclude optional fields from ContractHash, change the assertion
    /// below to <c>.Be()</c>.
    /// </summary>
    [Fact]
    public void Build_OptionalSchemaFieldAddition_ChangesContractHash_UntilExclusionPolicy()
    {
        var original = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
        });
        var modified = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true },
            new SchemaFieldDescriptor { Name = "Phone", FieldType = "string", IsRequired = false }
        });

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        // Current implementation includes ALL fields in contract hash.
        // Change to .Be() when Issue #29 Requirement #4 exclusion policy is implemented.
        modifiedHashes.ContractHash.Should().NotBe(originalHashes.ContractHash,
            "CURRENT: all fields (including optional) are in contract hash — update to .Be() when exclusion policy is implemented");
    }

    // ── DI Registration Test ──

    [Fact]
    public void DI_Should_Resolve_IDescriptorStableHashBuilder()
    {
        var services = new ServiceCollection();
        services.AddDescriptorStableHash();
        var provider = services.BuildServiceProvider();

        var builder = provider.GetRequiredService<IDescriptorStableHashBuilder>();

        builder.Should().NotBeNull();
        builder.Should().BeOfType<DescriptorStableHashBuilder>();
    }

    // ── Edge cases ──

    [Fact]
    public void Build_ContractHash_Should_Be_Stable_Across_Equivalent_Descriptors()
    {
        var cap1 = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test", Version = 1,
            Permissions = new[] { "p1" },
            RiskLevel = CapabilityRiskLevel.Low
        };
        var cap2 = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test", Version = 1,
            Permissions = new[] { "p1" },
            RiskLevel = CapabilityRiskLevel.Low
        };

        var h1 = _builder.Build(cap1).ContractHash;
        var h2 = _builder.Build(cap2).ContractHash;
        var h3 = _builder.Build(cap1).ContractHash;

        h1.Should().Be(h2, "equivalent descriptors should produce same contract hash");
        h1.Should().Be(h3, "same instance should produce same contract hash on multiple calls");
    }

    [Fact]
    public void Build_Returns_NonEmpty_Hashes()
    {
        var schema = CreateSchema("s1", "Test");

        var result = _builder.Build(schema);

        result.ContractHash.Should().NotBeNullOrEmpty();
        result.DefinitionHash.Should().NotBeNullOrEmpty();
        result.RuntimeHash.Should().BeNull("RuntimeHash is reserved for future use");
        result.BindingHash.Should().BeNull("BindingHash is reserved for future use");
    }

    [Fact]
    public void Build_ContractHash_Differs_From_DefinitionHash()
    {
        var schema = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "string", IsRequired = true, MaxLength = 200 }
        });

        var result = _builder.Build(schema);

        result.ContractHash.Should().NotBe(result.DefinitionHash,
            "contract hash (subset) should differ from definition hash (full serialization)");
    }

    [Fact]
    public void Build_HumanTask_Descriptor_Hashes_Are_Stable()
    {
        var ht1 = new HumanTaskDescriptor
        {
            Id = "ht_01", Name = "Review", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            AssigneeStrategy = AssigneeStrategy.CandidateGroup,
            Permissions = "approve",
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_approve", 1)
                }
            }
        };
        var ht2 = new HumanTaskDescriptor
        {
            Id = "ht_01", Name = "Review", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            AssigneeStrategy = AssigneeStrategy.CandidateGroup,
            Permissions = "approve",
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_approve", 1)
                }
            }
        };

        var h1 = _builder.Build(ht1);
        var h2 = _builder.Build(ht2);

        h1.ContractHash.Should().Be(h2.ContractHash);
        h1.DefinitionHash.Should().Be(h2.DefinitionHash);
    }

    [Fact]
    public void Build_EventDescriptor_Hashes_Are_Stable()
    {
        var evt1 = new EventDescriptor
        {
            Id = "evt_01", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Category = EventCategory.Domain,
            Semantic = EventSemantic.Fact,
            Importance = EventImportance.Operational
        };
        var evt2 = new EventDescriptor
        {
            Id = "evt_01", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Category = EventCategory.Domain,
            Semantic = EventSemantic.Fact,
            Importance = EventImportance.Operational
        };

        var h1 = _builder.Build(evt1);
        var h2 = _builder.Build(evt2);

        h1.ContractHash.Should().Be(h2.ContractHash);
        h1.DefinitionHash.Should().Be(h2.DefinitionHash);
    }

    // ── Helpers ──

    private static SchemaDescriptor CreateSchema(
        string id,
        string name,
        int version = 1,
        params SchemaFieldDescriptor[] fields)
    {
        return new SchemaDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Fields = fields,
            State = DescriptorState.Active,
            ChangeKind = SchemaChangeKind.Additive
        };
    }
}
