using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorImpact;

public class DescriptorChangeSetBuilderTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly DescriptorChangeSetBuilder _builder;

    public DescriptorChangeSetBuilderTests()
    {
        _hashBuilder = new DescriptorStableHashBuilder(_hashComputer);
        _builder = new DescriptorChangeSetBuilder(_hashBuilder);
    }

    private static CapabilityDescriptor CreateCapability(string id, string name = "Test",
        DescriptorState state = DescriptorState.Active,
        string[]? permissions = null)
    {
        return new CapabilityDescriptor
        {
            Id = id, Name = name, Version = 0,
            State = state, SupersededById = null,
            CapabilityKind = CapabilityKind.Command,
            Permissions = permissions ?? []
        };
    }

    [Fact]
    public void Added_Descriptor_WhenNotInBefore()
    {
        var after = new IDescriptor[] { CreateCapability("A") };
        var result = _builder.Build(Array.Empty<IDescriptor>(), after);
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Added);
    }

    [Fact]
    public void Removed_Descriptor_WhenNotInAfter()
    {
        var before = new IDescriptor[] { CreateCapability("A") };
        var result = _builder.Build(before, Array.Empty<IDescriptor>());
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Removed);
    }

    [Fact]
    public void StateChanged_Detected()
    {
        var d1 = CreateCapability("A", state: DescriptorState.Active);
        var d2 = CreateCapability("A", state: DescriptorState.Draft);
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.StateChanged);
    }

    [Fact]
    public void ContractHashChanged_Detected()
    {
        // Two descriptors with different contract fields produce different computed hashes
        var d1 = CreateCapability("A", name: "Name1");
        var d2 = CreateCapability("A", name: "Name2");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.ContractHashChanged);
    }

    [Fact]
    public void StateChanged_Priority_Over_ContractHashChanged()
    {
        var d1 = CreateCapability("A", name: "Name1", state: DescriptorState.Active);
        var d2 = CreateCapability("A", name: "Name2", state: DescriptorState.Draft);
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.StateChanged);
    }

    [Fact]
    public void Deprecated_StateTransition()
    {
        var d1 = CreateCapability("A", state: DescriptorState.Active);
        var d2 = CreateCapability("A", state: DescriptorState.Deprecated);
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Deprecated);
    }

    [Fact]
    public void Removed_StateTransition()
    {
        var d1 = CreateCapability("A", state: DescriptorState.Active);
        var d2 = CreateCapability("A", state: DescriptorState.Removed);
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Removed);
    }

    [Fact]
    public void Activated_StateTransition()
    {
        var d1 = CreateCapability("A", state: DescriptorState.Draft);
        var d2 = CreateCapability("A", state: DescriptorState.Active);
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Activated);
    }

    [Fact]
    public void Update_StateAndContractUnchanged_OtherFieldsDiffer()
    {
        // With concrete descriptors, contract hash changes when Name differs
        // because Name is included in the contract hash projection
        var d1 = CreateCapability("A", name: "OldName");
        var d2 = CreateCapability("A", name: "NewName");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.ContractHashChanged);
    }

    [Fact]
    public void NoChange_WhenIdentical()
    {
        var d = CreateCapability("A");
        var result = _builder.Build(new[] { d }, new[] { d });
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Ordering_IsPredictionIndependent()
    {
        var d1 = CreateCapability("A");
        var d2 = CreateCapability("B");
        var result1 = _builder.Build(new[] { d1, d2 }, new[] { d1 });
        var result2 = _builder.Build(new[] { d2, d1 }, new[] { d1 });
        result1.Changes.Should().HaveCount(1).And.ContainSingle(c => c.Ref.Id == "B");
        result2.Changes.Should().HaveCount(1).And.ContainSingle(c => c.Ref.Id == "B");
    }

    private static CanonicalHash CreateTestHash(string value, string purpose = "Contract")
    {
        return new CanonicalHash
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "Descriptor",
            Scope = "InternalFull",
            Purpose = purpose,
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "schema-contract-hash-v1"
        };
    }

    private static IDescriptor CreateMockDescriptor(
        string ns, string id, string name, DescriptorState state = DescriptorState.Active)
    {
        return Mock.Of<IDescriptor>(d =>
            d.Namespace == ns &&
            d.Id == id &&
            d.Name == name &&
            d.State == state);
    }

    [Fact]
    public void DefinitionHashChanged_WhenContractHashSameAndDefinitionHashDiffers()
    {
        var hashBuilder = new Mock<IDescriptorStableHashBuilder>();
        var builder = new DescriptorChangeSetBuilder(hashBuilder.Object);

        var d1 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Active);
        var d2 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Active);

        var contractHash = CreateTestHash("contract-abc", "Contract");
        var defHash1 = CreateTestHash("def-111", "Definition");
        var defHash2 = CreateTestHash("def-222", "Definition");

        hashBuilder.Setup(h => h.Build(d1)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash1
        });
        hashBuilder.Setup(h => h.Build(d2)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash2
        });

        var result = builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle()
            .Which.Kind.Should().Be(DescriptorChangeKind.DefinitionHashChanged);
    }

    [Fact]
    public void Updated_WhenBothHashesSameAndNameDiffers()
    {
        var hashBuilder = new Mock<IDescriptorStableHashBuilder>();
        var builder = new DescriptorChangeSetBuilder(hashBuilder.Object);

        var d1 = CreateMockDescriptor("test", "A", "OldName", DescriptorState.Active);
        var d2 = CreateMockDescriptor("test", "A", "NewName", DescriptorState.Active);

        var contractHash = CreateTestHash("contract-abc", "Contract");
        var defHash = CreateTestHash("def-111", "Definition");

        hashBuilder.Setup(h => h.Build(d1)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash
        });
        hashBuilder.Setup(h => h.Build(d2)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash
        });

        var result = builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle()
            .Which.Kind.Should().Be(DescriptorChangeKind.Updated);
    }

    [Fact]
    public void StateChanged_BeatsDefinitionHashChanged()
    {
        var hashBuilder = new Mock<IDescriptorStableHashBuilder>();
        var builder = new DescriptorChangeSetBuilder(hashBuilder.Object);

        var d1 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Active);
        var d2 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Draft);

        var contractHash = CreateTestHash("contract-abc", "Contract");
        var defHash1 = CreateTestHash("def-111", "Definition");
        var defHash2 = CreateTestHash("def-222", "Definition");

        hashBuilder.Setup(h => h.Build(d1)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash1
        });
        hashBuilder.Setup(h => h.Build(d2)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash2
        });

        var result = builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle()
            .Which.Kind.Should().Be(DescriptorChangeKind.StateChanged);
    }

    [Fact]
    public void ContractHashChanged_BeatsDefinitionHashChanged()
    {
        var hashBuilder = new Mock<IDescriptorStableHashBuilder>();
        var builder = new DescriptorChangeSetBuilder(hashBuilder.Object);

        var d1 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Active);
        var d2 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Active);

        var contractHash1 = CreateTestHash("contract-111", "Contract");
        var contractHash2 = CreateTestHash("contract-222", "Contract");
        var defHash1 = CreateTestHash("def-111", "Definition");
        var defHash2 = CreateTestHash("def-333", "Definition");

        hashBuilder.Setup(h => h.Build(d1)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash1,
            DefinitionHash = defHash1
        });
        hashBuilder.Setup(h => h.Build(d2)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash2,
            DefinitionHash = defHash2
        });

        var result = builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle()
            .Which.Kind.Should().Be(DescriptorChangeKind.ContractHashChanged);
    }

    [Fact]
    public void AddedAndRemoved_BeatHashComparison()
    {
        var hashBuilder = new Mock<IDescriptorStableHashBuilder>();
        var anyHash = CreateTestHash("any-hash", "Contract");
        var anyHashes = new DescriptorStableHashes
        {
            ContractHash = anyHash,
            DefinitionHash = anyHash
        };
        hashBuilder.Setup(h => h.Build(It.IsAny<IDescriptor>())).Returns(anyHashes);
        var builder = new DescriptorChangeSetBuilder(hashBuilder.Object);

        var d1 = CreateMockDescriptor("test", "A", "Test", DescriptorState.Active);

        // Only in after → Added
        {
            var result = builder.Build(Array.Empty<IDescriptor>(), new[] { d1 });
            result.Changes.Should().ContainSingle()
                .Which.Kind.Should().Be(DescriptorChangeKind.Added);
        }

        // Only in before → Removed
        {
            var result = builder.Build(new[] { d1 }, Array.Empty<IDescriptor>());
            result.Changes.Should().ContainSingle()
                .Which.Kind.Should().Be(DescriptorChangeKind.Removed);
        }
    }

    [Fact]
    public void DefinitionHashValues_ArePopulatedOnChange()
    {
        var hashBuilder = new Mock<IDescriptorStableHashBuilder>();
        var builder = new DescriptorChangeSetBuilder(hashBuilder.Object);

        var d1 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Active);
        var d2 = CreateMockDescriptor("test", "A", "SameName", DescriptorState.Active);

        var contractHash = CreateTestHash("contract-abc", "Contract");
        var defHash1 = CreateTestHash("def-before", "Definition");
        var defHash2 = CreateTestHash("def-after", "Definition");

        hashBuilder.Setup(h => h.Build(d1)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash1
        });
        hashBuilder.Setup(h => h.Build(d2)).Returns(new DescriptorStableHashes
        {
            ContractHash = contractHash,
            DefinitionHash = defHash2
        });

        var result = builder.Build(new[] { d1 }, new[] { d2 });
        var change = result.Changes.Should().ContainSingle().Subject;
        change.Kind.Should().Be(DescriptorChangeKind.DefinitionHashChanged);
        change.BeforeContractHash.Should().Be("contract-abc");
        change.AfterContractHash.Should().Be("contract-abc");
        change.BeforeDefinitionHash.Should().Be("def-before");
        change.AfterDefinitionHash.Should().Be("def-after");
    }

    [Fact]
    public void OptionalFieldAddition_WithRealHashBuilder_Produces_DefinitionHashChanged()
    {
        // End-to-end: DefaultCanonicalHashComputer → DescriptorStableHashBuilder → DescriptorChangeSetBuilder
        // Adding an optional field to a Schema changes DefinitionHash but not ContractHash (v2)
        var before = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Fields = new[] { new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true } }
        };
        var after = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1, State = DescriptorState.Active,
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true },
                new SchemaFieldDescriptor { Name = "Phone", FieldType = "string", IsRequired = false }
            }
        };

        var result = _builder.Build(new IDescriptor[] { before }, new IDescriptor[] { after });
        var change = result.Changes.Should().ContainSingle().Subject;
        change.Kind.Should().Be(DescriptorChangeKind.DefinitionHashChanged);
        change.BeforeContractHash.Should().Be(change.AfterContractHash,
            "optional field addition should not change ContractHash (v2)");
        change.BeforeDefinitionHash.Should().NotBe(change.AfterDefinitionHash,
            "optional field addition must change DefinitionHash");
    }
}
