using System.Text.Json;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing.Generated;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public sealed class DescriptorStableHashBuilderTests
{
    private readonly DefaultCanonicalHashComputer _hashComputer = new();
    private readonly DescriptorStableHashBuilder _builder;

    public DescriptorStableHashBuilderTests()
    {
        _builder = new DescriptorStableHashBuilder(_hashComputer);
    }

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
        // v2: ContractHash no longer changes because optional fields are excluded from the contract binding surface
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
    /// Verifies v2 behavior: adding an optional field does NOT change ContractHash
    /// (only the required-binding surface participates in ContractHash).
    /// </summary>
    [Fact]
    public void Build_OptionalSchemaFieldAddition_DoesNotChangeContractHash()
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

        // v2: optional fields are excluded from ContractHash
        modifiedHashes.ContractHash.Value.Should().Be(originalHashes.ContractHash.Value,
            "optional field addition should NOT change ContractHash (v2)");
    }

    [Fact]
    public void Build_WorkflowStepReorder_Should_ChangeContractHash()
    {
        var original = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep { Id = "step_a", Name = "A", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() },
                new WorkflowStep { Id = "step_b", Name = "B", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_y", 1) }, Transitions = Array.Empty<string>() },
            }
        };
        var reordered = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep { Id = "step_b", Name = "B", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_y", 1) }, Transitions = Array.Empty<string>() },
                new WorkflowStep { Id = "step_a", Name = "A", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() },
            }
        };

        var h1 = _builder.Build(original);
        var h2 = _builder.Build(reordered);

        h1.ContractHash.Should().NotBe(h2.ContractHash, "step order is runtime-semantic — reordering must change contract hash");
    }

    [Fact]
    public void Build_WorkflowStepReorder_Should_ChangeDefinitionHash()
    {
        var original = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep { Id = "step_a", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() },
                new WorkflowStep { Id = "step_b", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_y", 1) }, Transitions = Array.Empty<string>() },
            }
        };
        var reordered = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            Steps = new[]
            {
                new WorkflowStep { Id = "step_b", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_y", 1) }, Transitions = Array.Empty<string>() },
                new WorkflowStep { Id = "step_a", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() },
            }
        };

        var h1 = _builder.Build(original);
        var h2 = _builder.Build(reordered);

        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash, "step order is runtime-semantic — reordering must change definition hash");
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

        result.ContractHash.Should().NotBeNull();
        result.ContractHash.Value.Should().NotBeNullOrEmpty();
        result.DefinitionHash.Should().NotBeNull();
        result.DefinitionHash.Value.Should().NotBeNullOrEmpty();
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
            "contract hash (subset) should differ from definition hash (explicit field enumeration)");
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

    // ── Per-kind behavior tests: Contract vs DefinitionOnly ──

    [Fact]
    public void Changing_SchemaValidationRule_Should_ChangeDefinitionHashOnly()
    {
        var s1 = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Fields = new[] { new SchemaFieldDescriptor { Name = "Name", FieldType = "string" } },
            ValidationRules = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 0" } }
        };
        var s2 = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Fields = new[] { new SchemaFieldDescriptor { Name = "Name", FieldType = "string" } },
            ValidationRules = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 100" } }
        };

        var h1 = _builder.Build(s1);
        var h2 = _builder.Build(s2);

        h1.ContractHash.Should().Be(h2.ContractHash, "validation rule change is definition-only");
        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash, "validation rule change must change definition hash");
    }

    [Fact]
    public void Changing_CapabilityCategory_Should_ChangeDefinitionHashOnly()
    {
        var c1 = new CapabilityDescriptor { Id = "c1", Name = "test", Version = 1, Categories = new[] { "cat-a" } };
        var c2 = new CapabilityDescriptor { Id = "c1", Name = "test", Version = 1, Categories = new[] { "cat-b" } };

        var h1 = _builder.Build(c1);
        var h2 = _builder.Build(c2);

        h1.ContractHash.Should().Be(h2.ContractHash, "category change is definition-only metadata");
        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash);
    }

    [Fact]
    public void Changing_FormLayoutColumns_Should_ChangeDefinitionHashOnly()
    {
        var f1 = new FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            LayoutColumns = "2",
        };
        var f2 = new FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            LayoutColumns = "3",
        };

        var h1 = _builder.Build(f1);
        var h2 = _builder.Build(f2);

        h1.ContractHash.Should().Be(h2.ContractHash, "layout columns change is definition-only");
        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash);
    }

    [Fact]
    public void Changing_FormFieldLabel_Should_NotChangeContractHash()
    {
        var f1 = new FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Email", Label = "Email", Order = 0 } }
        };
        var f2 = new FormDescriptor
        {
            Id = "f1", Name = "Test", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Email", Label = "E-mail Address", Order = 0 } }
        };

        var h1 = _builder.Build(f1);
        var h2 = _builder.Build(f2);

        h1.ContractHash.Should().Be(h2.ContractHash, "label change is cosmetic — not in contract");
    }

    [Fact]
    public void Changing_HumanTaskTimeout_Should_ChangeDefinitionHashOnly()
    {
        var ht1 = new HumanTaskDescriptor
        {
            Id = "ht1", Name = "Test", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            Timeout = TimeSpan.FromHours(1)
        };
        var ht2 = new HumanTaskDescriptor
        {
            Id = "ht1", Name = "Test", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            Timeout = TimeSpan.FromHours(2)
        };

        var h1 = _builder.Build(ht1);
        var h2 = _builder.Build(ht2);

        h1.ContractHash.Should().Be(h2.ContractHash, "timeout change is definition-only operational metadata");
        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash);
    }

    [Fact]
    public void Changing_WorkflowStepName_Should_ChangeDefinitionHashOnly()
    {
        var w1 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1,
            Steps = new[] { new WorkflowStep { Id = "s1", Name = "Step A", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() } }
        };
        var w2 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1,
            Steps = new[] { new WorkflowStep { Id = "s1", Name = "Step B", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() } }
        };

        var h1 = _builder.Build(w1);
        var h2 = _builder.Build(w2);

        h1.ContractHash.Should().Be(h2.ContractHash, "step name is display metadata — not in contract");
        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash, "step name change must change definition hash");
    }

    [Fact]
    public void Changing_WorkflowStepCondition_Should_ChangeContractHash()
    {
        var w1 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1,
            Steps = new[] { new WorkflowStep { Id = "s1", Condition = "x > 0", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() } }
        };
        var w2 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1,
            Steps = new[] { new WorkflowStep { Id = "s1", Condition = "x > 100", Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_x", 1) }, Transitions = Array.Empty<string>() } }
        };

        var h1 = _builder.Build(w1);
        var h2 = _builder.Build(w2);

        h1.ContractHash.Should().NotBe(h2.ContractHash, "condition change affects runtime flow — must change contract hash");
    }

    [Fact]
    public void Changing_EventCategory_Should_ChangeContractHash()
    {
        var e1 = new EventDescriptor { Id = "e1", Name = "test", Version = 1, Category = EventCategory.Domain, PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1) };
        var e2 = new EventDescriptor { Id = "e1", Name = "test", Version = 1, Category = EventCategory.Integration, PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1) };

        var h1 = _builder.Build(e1);
        var h2 = _builder.Build(e2);

        h1.ContractHash.Should().NotBe(h2.ContractHash, "category change affects event routing — must change contract hash");
    }

    // ── InteractionTarget union profile behavior tests (Task 4) ──

    [Fact]
    public void Changing_WorkflowStep_Target_Kind_Should_ChangeContractHash()
    {
        // Changing from CapabilityTarget to HumanTaskTarget changes the discriminator kind.
        var wf1 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "s1", Name = "Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("ref-1", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };
        var wf2 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "s1", Name = "Step",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ref-1", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var h1 = _builder.Build(wf1);
        var h2 = _builder.Build(wf2);

        h1.ContractHash.Value.Should().NotBe(h2.ContractHash.Value,
            "changing target kind (Capability vs HumanTask) must change ContractHash");
        h1.DefinitionHash.Value.Should().NotBe(h2.DefinitionHash.Value,
            "changing target kind must change DefinitionHash");
    }

    [Fact]
    public void Changing_WorkflowStep_Target_Id_Should_ChangeContractHash()
    {
        // Same target kind (CapabilityTarget) with different capability id.
        var wf1 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "s1", Name = "Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-a", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };
        var wf2 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "s1", Name = "Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-b", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var h1 = _builder.Build(wf1);
        var h2 = _builder.Build(wf2);

        h1.ContractHash.Value.Should().NotBe(h2.ContractHash.Value,
            "changing target id within same kind must change ContractHash");
    }

    [Fact]
    public void Changing_WorkflowStep_Target_Version_Should_ChangeContractHash()
    {
        // Same target kind and id, different version.
        var wf1 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "s1", Name = "Step",
                    Target = new SubWorkflowTarget
                    {
                        SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("sub-wf", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };
        var wf2 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "s1", Name = "Step",
                    Target = new SubWorkflowTarget
                    {
                        SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("sub-wf", 2)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var h1 = _builder.Build(wf1);
        var h2 = _builder.Build(wf2);

        h1.ContractHash.Value.Should().NotBe(h2.ContractHash.Value,
            "changing target version must change ContractHash");
    }

    [Fact]
    public void UnionProfile_Json_Shape_Contains_Kind_Before_Value()
    {
        // Verify that the generated union JSON for InteractionTarget has "Kind" before "Value".
        // We capture the canonical JSON bytes produced by the generated writer for a sub-structure
        // and verify that "Kind" appears before "Value" in the output.
        var target = new CapabilityTarget
        {
            Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-x", 1)
        };

        // Write canonical JSON to a buffer using the generated writer
        using var stream = new MemoryStream();
        using var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true
        });

        // Write just the sub-structure payload (no envelope)
        CanonicalHashing.Generated.InteractionTargetCanonicalHashWriter.WriteContractPayload(jsonWriter, target);
        jsonWriter.Flush();

        stream.Position = 0;
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        var kindIndex = json.IndexOf("\"Kind\"", StringComparison.Ordinal);
        var valueIndex = json.IndexOf("\"Value\"", StringComparison.Ordinal);

        kindIndex.Should().BeGreaterThan(-1, "output must contain discriminator 'Kind'");
        valueIndex.Should().BeGreaterThan(-1, "output must contain payload 'Value'");
        kindIndex.Should().BeLessThan(valueIndex,
            "'Kind' must appear before 'Value' in union JSON shape");
    }

    [Fact]
    public void Changing_HumanTaskAssigneeStrategy_Should_ChangeContractHash()
    {
        var ht1 = new HumanTaskDescriptor { Id = "ht1", Name = "test", Version = 1, Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("f1", 1), AssigneeStrategy = AssigneeStrategy.SingleUser };
        var ht2 = new HumanTaskDescriptor { Id = "ht1", Name = "test", Version = 1, Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("f1", 1), AssigneeStrategy = AssigneeStrategy.CandidateGroup };

        var h1 = _builder.Build(ht1);
        var h2 = _builder.Build(ht2);

        h1.ContractHash.Should().NotBe(h2.ContractHash, "assignee strategy affects task distribution — must change contract hash");
    }

    [Fact]
    public void Changing_FormControlType_Should_ChangeContractHash()
    {
        var f1 = new FormDescriptor { Id = "f1", Name = "test", Version = 1, Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1), Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Email", ControlType = "text", Order = 0 } } };
        var f2 = new FormDescriptor { Id = "f1", Name = "test", Version = 1, Schema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1), Fields = new[] { new FormFieldDescriptor { SchemaFieldName = "Email", ControlType = "select", Order = 0 } } };

        var h1 = _builder.Build(f1);
        var h2 = _builder.Build(f2);

        h1.ContractHash.Should().NotBe(h2.ContractHash, "control type change affects interaction contract — must change contract hash");
    }

    // ── Tie-breaker tests: duplicate-key order insensitivity ──

    [Fact]
    public void SchemaReferences_SameId_DifferentVersion_OrderInsensitive()
    {
        var refs1 = new[]
        {
            new VersionedDescriptorRef<SchemaDescriptor>("s1", 2),
            new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
        };
        var refs2 = new[]
        {
            new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            new VersionedDescriptorRef<SchemaDescriptor>("s1", 2),
        };
        var s1 = CreateSchema("s1", "Test", fields: new[] { new SchemaFieldDescriptor { Name = "N", FieldType = "string" } });
        var s2 = CreateSchema("s1", "Test", fields: new[] { new SchemaFieldDescriptor { Name = "N", FieldType = "string" } });
        s1 = new SchemaDescriptor { Id = s1.Id, Name = s1.Name, Version = s1.Version, State = s1.State, Fields = s1.Fields, References = refs1 };
        s2 = new SchemaDescriptor { Id = s2.Id, Name = s2.Name, Version = s2.Version, State = s2.State, Fields = s2.Fields, References = refs2 };

        var h1 = _builder.Build(s1);
        var h2 = _builder.Build(s2);

        h1.ContractHash.Should().Be(h2.ContractHash, "references order must be transparent to contract hash");
        h1.DefinitionHash.Should().Be(h2.DefinitionHash, "references with same Id but different Version must be order-insensitive");
    }

    [Fact]
    public void ValidationRules_SameName_DifferentExpression_OrderInsensitive()
    {
        var rules1 = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 100" }, new SchemaValidationRule { Name = "r1", Expression = "x > 0" } };
        var rules2 = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 0" }, new SchemaValidationRule { Name = "r1", Expression = "x > 100" } };
        var s1 = new SchemaDescriptor { Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active, ValidationRules = rules1 };
        var s2 = new SchemaDescriptor { Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active, ValidationRules = rules2 };

        var h1 = _builder.Build(s1).DefinitionHash;
        var h2 = _builder.Build(s2).DefinitionHash;

        h1.Should().Be(h2, "validation rules with same Name, different Expression must be order-insensitive");
    }

    [Fact]
    public void HumanTaskOutcomes_SameCondition_DifferentCapability_OrderInsensitive()
    {
        var outcomes1 = new[]
        {
            new CompletionOutcome { Condition = CompletionCondition.Approve, Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) },
            new CompletionOutcome { Condition = CompletionCondition.Approve, Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) },
        };
        var outcomes2 = new[]
        {
            new CompletionOutcome { Condition = CompletionCondition.Approve, Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) },
            new CompletionOutcome { Condition = CompletionCondition.Approve, Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) },
        };
        var ht1 = new HumanTaskDescriptor { Id = "ht1", Name = "Test", Version = 1, Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("f1", 1), Outcomes = outcomes1 };
        var ht2 = new HumanTaskDescriptor { Id = "ht1", Name = "Test", Version = 1, Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("f1", 1), Outcomes = outcomes2 };

        var h1 = _builder.Build(ht1);
        var h2 = _builder.Build(ht2);

        h1.ContractHash.Should().Be(h2.ContractHash, "outcomes order must be transparent to contract hash");
        h1.DefinitionHash.Should().Be(h2.DefinitionHash, "outcomes with same Condition, different Capability must be order-insensitive");
    }

    [Fact]
    public void ValidationRules_SameNameExpression_DifferentErrorMessage_OrderInsensitive()
    {
        var rules1 = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 0", ErrorMessage = "Too high" }, new SchemaValidationRule { Name = "r1", Expression = "x > 0", ErrorMessage = "Too low" } };
        var rules2 = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 0", ErrorMessage = "Too low" }, new SchemaValidationRule { Name = "r1", Expression = "x > 0", ErrorMessage = "Too high" } };
        var s1 = new SchemaDescriptor { Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active, ValidationRules = rules1 };
        var s2 = new SchemaDescriptor { Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active, ValidationRules = rules2 };

        var h1 = _builder.Build(s1).DefinitionHash;
        var h2 = _builder.Build(s2).DefinitionHash;

        h1.Should().Be(h2, "validation rules with same Name/Expression, different ErrorMessage must be order-insensitive");
    }

    // ── Domain Separation Tests (Issue #30 acceptance criteria) ──
    // Same payload content, different metadata → different hash.
    // This proves the canonical JSON envelope includes domain-separation metadata
    // (ArtifactKind, DescriptorKind, Scope, Purpose) in the hash input.

    [Fact]
    public void DomainSeparation_DifferentArtifactKind_ProducesDifferentHash()
    {
        var schema = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "string", IsRequired = true }
        });

        var contractHash = _builder.Build(schema).ContractHash;
        contractHash.ArtifactKind.Should().Be("Descriptor");

        // ComputeFromProjection with different ArtifactKind but same payload writer
        var original = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);
        var differentArtifactKind = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = "Package",
                DescriptorKind = null,
                Purpose = original.Purpose,
                Scope = original.Scope,
                AlgorithmVersion = original.AlgorithmVersion,
                ContractVersion = original.ContractVersion,
                CanonicalShapeVersion = original.CanonicalShapeVersion
            },
            // Use a simple writer that writes the same payload content
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("Payload", "same-content");
                writer.WriteEndObject();
            });

        var differentHash = _hashComputer.ComputeFromProjection(differentArtifactKind);
        differentHash.Value.Should().NotBe(original.Value,
            "different ArtifactKind must produce different hash even with same payload content");
    }

    [Fact]
    public void DomainSeparation_DifferentDescriptorKind_ProducesDifferentHash()
    {
        var schema = CreateSchema("s1", "Test");
        var form = new FormDescriptor { Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active };

        var schemaHash = _builder.Build(schema).ContractHash;
        var formHash = _builder.Build(form).ContractHash;

        schemaHash.DescriptorKind.Should().Be("Schema");
        formHash.DescriptorKind.Should().Be("Form");
        schemaHash.Value.Should().NotBe(formHash.Value,
            "different DescriptorKind must produce different hash");
    }

    [Fact]
    public void DomainSeparation_DifferentScope_ProducesDifferentHash()
    {
        var schema = CreateSchema("s1", "Test");

        var internalHash = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);
        var tenantHash = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.TenantVisible);

        internalHash.Scope.Should().Be("InternalFull");
        tenantHash.Scope.Should().Be("TenantVisible");
        internalHash.Value.Should().NotBe(tenantHash.Value,
            "different Scope must produce different hash");
    }

    [Fact]
    public void DomainSeparation_DifferentPurpose_ProducesDifferentHash()
    {
        var schema = CreateSchema("s1", "Test");

        var contractHash = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);
        var definitionHash = _hashComputer.ComputeDefinitionHash(schema, CanonicalHashScope.InternalFull);

        contractHash.Purpose.Should().Be("Contract");
        definitionHash.Purpose.Should().Be("Definition");
        contractHash.Value.Should().NotBe(definitionHash.Value,
            "different Purpose (Contract vs Definition) must produce different hash");
    }

    [Fact]
    public void WorkflowStep_TargetChange_ChangesContractHash()
    {
        // WorkflowStep.Target must participate in ContractHash —
        // connecting to a different capability must produce a different hash.
        var wf1 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "ws1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-1", 1) }
                }
            }
        };
        var wf2 = new WorkflowDescriptor
        {
            Id = "wf1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "ws1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-2", 1) }
                }
            }
        };

        var h1 = _builder.Build(wf1).ContractHash;
        var h2 = _builder.Build(wf2).ContractHash;

        h1.Value.Should().NotBe(h2.Value,
            "different WorkflowStep.Target (different capability) must produce different ContractHash");
    }

    // ── EventDescriptor contract behavior (spec §8.2) ──

    [Fact]
    public void Changing_EventPayloadSchema_Should_ChangeContractHash()
    {
        var e1 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema-a", 1),
            Category = EventCategory.Domain
        };
        var e2 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema-b", 1),
            Category = EventCategory.Domain
        };

        var h1 = _builder.Build(e1);
        var h2 = _builder.Build(e2);

        h1.ContractHash.Should().NotBe(h2.ContractHash,
            "changing PayloadSchema (event data contract) must change ContractHash");
        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash);
    }

    [Fact]
    public void Changing_EventSemantic_Should_ChangeContractHash()
    {
        var e1 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Semantic = EventSemantic.Fact
        };
        var e2 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Semantic = EventSemantic.StateTransition
        };

        var h1 = _builder.Build(e1);
        var h2 = _builder.Build(e2);

        h1.ContractHash.Should().NotBe(h2.ContractHash,
            "changing event semantic must change ContractHash");
    }

    [Fact]
    public void Changing_EventImportance_Should_ChangeDefinitionHashOnly()
    {
        var e1 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Importance = EventImportance.Operational
        };
        var e2 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            Importance = EventImportance.Critical
        };

        var h1 = _builder.Build(e1);
        var h2 = _builder.Build(e2);

        h1.ContractHash.Should().Be(h2.ContractHash,
            "importance change is definition-only (infrastructure policy, not contract structure)");
        h1.DefinitionHash.Should().NotBe(h2.DefinitionHash,
            "importance change must change DefinitionHash");
    }

    [Fact]
    public void Changing_EventChangeKind_Should_ChangeContractHash()
    {
        var e1 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            ChangeKind = SchemaChangeKind.Additive
        };
        var e2 = new EventDescriptor
        {
            Id = "e1", Name = "test.event", Version = 1,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("s1", 1),
            ChangeKind = SchemaChangeKind.Breaking
        };

        var h1 = _builder.Build(e1);
        var h2 = _builder.Build(e2);

        h1.ContractHash.Should().NotBe(h2.ContractHash,
            "changing event ChangeKind must change ContractHash");
    }

    // ── CanonicalHashProjectionResult Invariant Tests ──

    [Fact]
    public void ProjectionResult_Create_WithNullMetadata_Throws()
    {
        var act = () => CanonicalHashProjectionResult.Create(null!, _ => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ProjectionResult_Create_WithNullWriter_Throws()
    {
        var metadata = new CanonicalHashMetadata
        {
            ArtifactKind = "Descriptor",
            DescriptorKind = "Schema",
            Purpose = "Contract",
            Scope = "InternalFull",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "schema-contract-hash-v1"
        };

        var act = () => CanonicalHashProjectionResult.Create(metadata, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Schema Canonical Hash v2 Behavior Tests (Task 5) ──

    [Fact]
    public void OptionalFieldAddition_DoesNotChange_SchemaContractHash()
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

        // v2: optional fields are excluded from ContractHash
        originalHashes.ContractHash.Value.Should().Be(modifiedHashes.ContractHash.Value,
            "optional field addition should NOT change Schema ContractHash (v2)");
    }

    [Fact]
    public void OptionalFieldAddition_Changes_SchemaDefinitionHash()
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

        // v2: optional fields are still included in DefinitionHash
        originalHashes.DefinitionHash.Value.Should().NotBe(modifiedHashes.DefinitionHash.Value,
            "optional field addition should change Schema DefinitionHash (v2)");
    }

    [Fact]
    public void RequiredFieldAddition_Changes_SchemaContractHash()
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

        // v2: required fields are still part of the contract binding surface
        originalHashes.ContractHash.Value.Should().NotBe(modifiedHashes.ContractHash.Value,
            "required field addition should change Schema ContractHash (v2)");
    }

    [Fact]
    public void OptionalFieldTypeChange_DoesNotChangeContractHash_ChangesDefinitionHash()
    {
        var original = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true },
            new SchemaFieldDescriptor { Name = "Phone", FieldType = "string", IsRequired = false }
        });
        var modified = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true },
            new SchemaFieldDescriptor { Name = "Phone", FieldType = "integer", IsRequired = false }
        });

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        originalHashes.ContractHash.Value.Should().Be(modifiedHashes.ContractHash.Value,
            "optional field type change should NOT change ContractHash (v2)");
        originalHashes.DefinitionHash.Value.Should().NotBe(modifiedHashes.DefinitionHash.Value,
            "optional field type change should change DefinitionHash (v2)");
    }

    [Fact]
    public void Build_RequiredFieldTypeChange_ChangesBothContractAndDefinitionHash()
    {
        var original = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "string", IsRequired = true }
        });
        var modified = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Email", FieldType = "integer", IsRequired = true }
        });

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        originalHashes.ContractHash.Value.Should().NotBe(modifiedHashes.ContractHash.Value,
            "required field type change should change ContractHash");
        originalHashes.DefinitionHash.Value.Should().NotBe(modifiedHashes.DefinitionHash.Value,
            "required field type change should change DefinitionHash");
    }

    [Fact]
    public void ValidationRuleChange_ChangesDefinitionHashOnly()
    {
        var original = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
            },
            ValidationRules = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 0" } }
        };
        var modified = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
            },
            ValidationRules = new[] { new SchemaValidationRule { Name = "r1", Expression = "x > 100" } }
        };

        var originalHashes = _builder.Build(original);
        var modifiedHashes = _builder.Build(modified);

        originalHashes.ContractHash.Value.Should().Be(modifiedHashes.ContractHash.Value,
            "validation rule change should NOT change ContractHash");
        originalHashes.DefinitionHash.Value.Should().NotBe(modifiedHashes.DefinitionHash.Value,
            "validation rule change should change DefinitionHash");
    }

    // ── Golden-master: WorkflowDescriptor union profile hash determinism (Finding 4) ──

    [Fact]
    public void WorkflowDescriptor_UnionProfileTarget_HashValuesAreDeterministic()
    {
        // 1. Create a fixed WorkflowDescriptor with known Steps (at least one step with a CapabilityTarget)
        var wf1 = new WorkflowDescriptor
        {
            Id = "wf-determinism", Name = "determinism-test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01", Name = "Step One",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-det", 1)
                    },
                    Condition = "x > 0",
                    Transitions = new[] { "approve", "reject" }
                },
                new WorkflowStep
                {
                    Id = "step_02", Name = "Step Two",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-det-2", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        // 2. Compute ContractHash and DefinitionHash via ICanonicalHashComputer
        var contractHash1 = _hashComputer.ComputeContractHash(wf1, CanonicalHashScope.InternalFull);
        var definitionHash1 = _hashComputer.ComputeDefinitionHash(wf1, CanonicalHashScope.InternalFull);

        // 3. Assert both hash values are non-null/non-empty
        contractHash1.Should().NotBeNull();
        contractHash1.Value.Should().NotBeNullOrEmpty();
        definitionHash1.Should().NotBeNull();
        definitionHash1.Value.Should().NotBeNullOrEmpty();

        // 4. Create a second identical descriptor, compute hashes, assert they're identical (determinism)
        var wf2 = new WorkflowDescriptor
        {
            Id = "wf-determinism", Name = "determinism-test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01", Name = "Step One",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-det", 1)
                    },
                    Condition = "x > 0",
                    Transitions = new[] { "approve", "reject" }
                },
                new WorkflowStep
                {
                    Id = "step_02", Name = "Step Two",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-det-2", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var contractHash2 = _hashComputer.ComputeContractHash(wf2, CanonicalHashScope.InternalFull);
        var definitionHash2 = _hashComputer.ComputeDefinitionHash(wf2, CanonicalHashScope.InternalFull);

        contractHash1.Value.Should().Be(contractHash2.Value,
            "identical descriptors must produce identical ContractHash (determinism)");
        definitionHash1.Value.Should().Be(definitionHash2.Value,
            "identical descriptors must produce identical DefinitionHash (determinism)");

        // 5. Create a descriptor with a different Target (HumanTaskTarget instead of CapabilityTarget), assert hashes differ
        var wf3 = new WorkflowDescriptor
        {
            Id = "wf-determinism", Name = "determinism-test", Version = 1, State = DescriptorState.Active,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01", Name = "Step One",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht-det", 1)
                    },
                    Condition = "x > 0",
                    Transitions = new[] { "approve", "reject" }
                },
                new WorkflowStep
                {
                    Id = "step_02", Name = "Step Two",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap-det-2", 1)
                    },
                    Transitions = Array.Empty<string>()
                }
            }
        };

        var contractHash3 = _hashComputer.ComputeContractHash(wf3, CanonicalHashScope.InternalFull);

        contractHash3.Value.Should().NotBe(contractHash1.Value,
            "different target type (HumanTask vs Capability) must produce different ContractHash");
    }

    // ── Double Culture Stability ──

    [Fact]
    public void SchemaField_DoubleMaxValue_HashStableAcrossCultures()
    {
        // Verify that double? MaxValue/MinValue fields produce deterministic canonical hash
        // regardless of thread culture (no ToString() culture sensitivity).
        var schema = CreateSchema("s1", "Test", fields: new[]
        {
            new SchemaFieldDescriptor { Name = "Amount", FieldType = "double", MaxValue = 99.99, MinValue = 0.01 }
        });

        var hashDefault = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);

        // Switch to a culture that uses comma as decimal separator
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            var hashGerman = _hashComputer.ComputeContractHash(schema, CanonicalHashScope.InternalFull);

            hashGerman.Value.Should().Be(hashDefault.Value,
                "double values must produce identical hash bytes regardless of CurrentCulture");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
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
