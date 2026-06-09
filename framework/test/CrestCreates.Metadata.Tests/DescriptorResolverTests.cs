using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorResolverTests
{
    private class TestDescriptor : IDescriptor, IVersionedDescriptor
    {
        public string Namespace { get; init; } = "test";
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Version { get; init; }
        public DescriptorKind Kind { get; init; }
        public DescriptorState State { get; init; } = DescriptorState.Active;
        public string ContractHash { get; init; } = string.Empty;
        public string DefinitionHash { get; init; } = string.Empty;
        public string? SupersededById { get; init; }
    }

    [Fact]
    public void Resolve_by_id_returns_descriptor()
    {
        var resolver = new DescriptorResolver(
            new Dictionary<Type, Func<string, IDescriptor?>>
            {
                [typeof(TestDescriptor)] = id => id == "a" ? new TestDescriptor { Id = "a", Name = "A", Version = 2 } : null
            });

        var result = resolver.Resolve<TestDescriptor>("a");

        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
    }

    [Fact]
    public void Resolve_returns_null_for_unknown()
    {
        var resolver = new DescriptorResolver(new Dictionary<Type, Func<string, IDescriptor?>>());

        var result = resolver.Resolve<TestDescriptor>("unknown");

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_by_IDescriptorRef_returns_descriptor()
    {
        var resolver = new DescriptorResolver(
            new Dictionary<Type, Func<string, IDescriptor?>>
            {
                [typeof(TestDescriptor)] = id => id == "b" ? new TestDescriptor { Id = "b", Name = "B", Version = 3 } : null
            });

        IDescriptorRef reference = new DescriptorRef("test", "b", 1);
        var result = resolver.Resolve<TestDescriptor>(reference);

        result.Should().NotBeNull();
        result!.Id.Should().Be("b");
        result.Version.Should().Be(3);
    }

    [Fact]
    public void Resolve_by_IDescriptorRef_returns_null_for_unknown()
    {
        var resolver = new DescriptorResolver(new Dictionary<Type, Func<string, IDescriptor?>>());

        IDescriptorRef reference = new DescriptorRef("test", "missing");
        var result = resolver.Resolve<TestDescriptor>(reference);

        result.Should().BeNull();
    }

    [Fact]
    public void Query_returns_empty_when_not_implemented()
    {
        var resolver = new DescriptorResolver(new Dictionary<Type, Func<string, IDescriptor?>>());

        var result = resolver.Query<TestDescriptor>(new DescriptorQuery());

        result.Should().BeEmpty();
    }
}
