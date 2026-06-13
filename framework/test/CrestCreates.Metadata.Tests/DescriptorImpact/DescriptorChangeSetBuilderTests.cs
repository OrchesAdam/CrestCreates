using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorImpact;

public class DescriptorChangeSetBuilderTests
{
    private readonly DescriptorChangeSetBuilder _builder = new();

    private sealed record StubDescriptor(
        string Namespace, string Id, string Name,
        DescriptorKind Kind, DescriptorState State, string ContractHash,
        int? Version = null) : IDescriptor, IVersionedDescriptor
    {
        public string FullId => $"{Namespace}.{Id}";
        public string DefinitionHash => "";
        public string? SupersededById => null;
        int IVersionedDescriptor.Version => Version ?? 0;
    }

    [Fact]
    public void Added_Descriptor_WhenNotInBefore()
    {
        var after = new IDescriptor[] { new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1") };
        var result = _builder.Build(Array.Empty<IDescriptor>(), after);
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Added);
    }

    [Fact]
    public void Removed_Descriptor_WhenNotInAfter()
    {
        var before = new IDescriptor[] { new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1") };
        var result = _builder.Build(before, Array.Empty<IDescriptor>());
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Removed);
    }

    [Fact]
    public void StateChanged_Detected()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Draft, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.StateChanged);
    }

    [Fact]
    public void ContractHashChanged_Detected()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash2");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.ContractHashChanged);
    }

    [Fact]
    public void StateChanged_Priority_Over_ContractHashChanged()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Draft, "hash2");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.StateChanged);
    }

    [Fact]
    public void Deprecated_StateTransition()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Deprecated, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Deprecated);
    }

    [Fact]
    public void Removed_StateTransition()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Removed, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Removed);
    }

    [Fact]
    public void Activated_StateTransition()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Draft, "hash1");
        var d2 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Activated);
    }

    [Fact]
    public void Update_StateAndContractUnchanged_OtherFieldsDiffer()
    {
        var d1 = new StubDescriptor("ns", "A", "OldName", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "A", "NewName", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result = _builder.Build(new[] { d1 }, new[] { d2 });
        result.Changes.Should().ContainSingle().Which.Kind.Should().Be(DescriptorChangeKind.Updated);
    }

    [Fact]
    public void NoChange_WhenIdentical()
    {
        var d = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result = _builder.Build(new[] { d }, new[] { d });
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Ordering_IsPredictionIndependent()
    {
        var d1 = new StubDescriptor("ns", "A", "A", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var d2 = new StubDescriptor("ns", "B", "B", DescriptorKind.Capability, DescriptorState.Active, "hash1");
        var result1 = _builder.Build(new[] { d1, d2 }, new[] { d1 });
        var result2 = _builder.Build(new[] { d2, d1 }, new[] { d1 });
        result1.Changes.Should().HaveCount(1).And.ContainSingle(c => c.Ref.Id == "B");
        result2.Changes.Should().HaveCount(1).And.ContainSingle(c => c.Ref.Id == "B");
    }
}
