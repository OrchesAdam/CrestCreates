using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class CapabilityHandlerResolverProviderTests
{
    [Fact]
    public void Register_AddsInvoker_ToSharedResolver()
    {
        // Arrange
        var invoker1 = new Mock<ICapabilityHandlerInvoker>().Object;
        var invoker2 = new Mock<ICapabilityHandlerInvoker>().Object;

        // Act
        CapabilityHandlerResolverProvider.Register("cap.1", invoker1);
        CapabilityHandlerResolverProvider.Register("cap.2", invoker2);

        // Assert
        var resolver = CapabilityHandlerResolverProvider.GetResolver();
        resolver.Should().NotBeNull();
        resolver.Resolve("cap.1").Should().BeSameAs(invoker1);
        resolver.Resolve("cap.2").Should().BeSameAs(invoker2);
    }

    [Fact]
    public void SetResolver_IsObsoleteNoOp()
    {
        // Arrange - register something first so we can verify it's not cleared
        var invoker = new Mock<ICapabilityHandlerInvoker>().Object;
        CapabilityHandlerResolverProvider.Register("cap.keep", invoker);

        // Act — should not throw
#pragma warning disable CS0618
        CapabilityHandlerResolverProvider.SetResolver(new Mock<ICapabilityHandlerResolver>().Object);
#pragma warning restore CS0618

        // Assert — previously registered handlers still available
        var resolver = CapabilityHandlerResolverProvider.GetResolver();
        resolver.Should().NotBeNull();
        resolver.Resolve("cap.keep").Should().BeSameAs(invoker);
    }
}
