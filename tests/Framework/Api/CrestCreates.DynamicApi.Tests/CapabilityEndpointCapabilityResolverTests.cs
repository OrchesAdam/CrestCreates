using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public sealed class CapabilityEndpointCapabilityResolverTests
{
    [Fact]
    public void Resolve_ExactVersion_ReturnsExactDescriptor()
    {
        // Arrange
        var descriptor = new CapabilityDescriptor
        {
            Id = "test-cap",
            Name = "Test Capability",
            Version = 2,
            State = DescriptorState.Active
        };

        var registry = new Mock<ICapabilityRegistry>();
        registry.Setup(r => r.GetByVersion("test-cap", 2)).Returns(descriptor);

        var capabilityRef = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap", 2);

        // Act
        var result = CapabilityEndpointCapabilityResolver.Resolve(registry.Object, capabilityRef);

        // Assert
        result.Should().BeSameAs(descriptor);
    }

    [Fact]
    public void Resolve_VersionZero_ReturnsLatestActive()
    {
        // Arrange
        var descriptor = new CapabilityDescriptor
        {
            Id = "test-cap",
            Name = "Test Capability",
            Version = 3,
            State = DescriptorState.Active
        };

        var registry = new Mock<ICapabilityRegistry>();
        registry.Setup(r => r.GetById("test-cap")).Returns(descriptor);

        var capabilityRef = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap", 0);

        // Act
        var result = CapabilityEndpointCapabilityResolver.Resolve(registry.Object, capabilityRef);

        // Assert
        result.Should().BeSameAs(descriptor);
        result.State.Should().Be(DescriptorState.Active);
    }

    [Fact]
    public void Resolve_NotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new Mock<ICapabilityRegistry>();
        registry.Setup(r => r.GetByVersion(It.IsAny<string>(), It.IsAny<int>())).Returns((CapabilityDescriptor?)null);
        registry.Setup(r => r.GetById(It.IsAny<string>())).Returns((CapabilityDescriptor?)null);
        registry.Setup(r => r.GetAll()).Returns(Array.Empty<CapabilityDescriptor>());

        var capabilityRef = new VersionedDescriptorRef<CapabilityDescriptor>("nonexistent", 0);

        // Act
        var act = () => CapabilityEndpointCapabilityResolver.Resolve(registry.Object, capabilityRef);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nonexistent*0*");
    }

    [Fact]
    public void ExactVersion_Missing_DoesNotFallbackToLatestActive()
    {
        // Arrange
        var registry = new Mock<ICapabilityRegistry>();
        var v1Descriptor = new CapabilityDescriptor
        {
            Id = "bk_create",
            Name = "Books Create",
            Version = 1,
            State = DescriptorState.Active
        };

        registry.Setup(r => r.GetByVersion("bk_create", 2)).Returns((CapabilityDescriptor?)null);
        registry.Setup(r => r.GetById("bk_create")).Returns(v1Descriptor);

        var capabilityRef = new VersionedDescriptorRef<CapabilityDescriptor>("bk_create", 2);

        // Act
        var act = () => CapabilityEndpointCapabilityResolver.Resolve(registry.Object, capabilityRef);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*version=2*");
    }
}
