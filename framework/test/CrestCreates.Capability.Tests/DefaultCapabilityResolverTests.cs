using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Internal;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class DefaultCapabilityResolverTests
{
    private sealed class TestCapabilityProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestCapabilityProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    private static CapabilityRegistry CreateRegistry(params CapabilityDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider(descriptors.ToList())]);
        return registry;
    }

    private static DefaultCapabilityResolver CreateResolver(CapabilityRegistry registry)
    {
        var versionResolver = new DefaultCapabilityVersionResolver(registry);
        return new DefaultCapabilityResolver(versionResolver);
    }

    [Fact]
    public void Resolve_ByCapabilityRef_WithVersion_ReturnsCorrectDescriptor()
    {
        var registry = CreateRegistry(
            new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 1 },
            new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 2 }
        );
        var resolver = CreateResolver(registry);

        var result = resolver.Resolve(new CapabilityRef { Id = "customer.create", Version = 2 });

        result.Id.Should().Be("customer.create");
        result.Version.Should().Be(2);
    }

    [Fact]
    public void Resolve_ByCapabilityRef_WithoutVersion_ReturnsActiveVersion()
    {
        var registry = CreateRegistry(
            new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 1 },
            new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 2, State = DescriptorState.Active }
        );
        var resolver = CreateResolver(registry);

        var result = resolver.Resolve(new CapabilityRef { Id = "customer.create" });

        result.Version.Should().Be(2);
    }

    [Fact]
    public void Resolve_ByCapabilityRef_NonExistent_ThrowsCapabilityNotFoundException()
    {
        var registry = CreateRegistry();
        var resolver = CreateResolver(registry);

        var act = () => resolver.Resolve(new CapabilityRef { Id = "nonexistent" });

        act.Should().Throw<CapabilityNotFoundException>()
            .WithMessage("*nonexistent*");
    }

    [Fact]
    public void Resolve_ByString_DelegatesToCapabilityRef()
    {
        var registry = CreateRegistry(
            new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 1 }
        );
        var resolver = CreateResolver(registry);

        var result = ((ICapabilityResolver)resolver).Resolve("customer.create");

        result.Id.Should().Be("customer.create");
    }

    [Fact]
    public void Resolve_ByCapabilityRef_NonExistentVersion_ThrowsCapabilityNotFoundException()
    {
        var registry = CreateRegistry(
            new CapabilityDescriptor { Id = "customer.create", Name = "Create Customer", Version = 1 }
        );
        var resolver = CreateResolver(registry);

        var act = () => resolver.Resolve(new CapabilityRef { Id = "customer.create", Version = 99 });

        act.Should().Throw<CapabilityNotFoundException>();
    }

    [Fact]
    public void Resolve_RespectsIdNotName_Distinction()
    {
        var registry = CreateRegistry(
            new CapabilityDescriptor { Id = "stable-id", Name = "Display Name A", Version = 1 },
            new CapabilityDescriptor { Id = "other-id", Name = "stable-id", Version = 1 }
        );
        var resolver = CreateResolver(registry);

        var result = ((ICapabilityResolver)resolver).Resolve("stable-id");

        result.Id.Should().Be("stable-id");
        result.Name.Should().Be("Display Name A");
    }
}
