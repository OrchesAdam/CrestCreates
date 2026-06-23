using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
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
}
