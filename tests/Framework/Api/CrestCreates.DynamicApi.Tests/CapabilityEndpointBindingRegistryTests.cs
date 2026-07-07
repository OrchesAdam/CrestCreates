using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public sealed class CapabilityEndpointBindingRegistryTests : IDisposable
{
    public CapabilityEndpointBindingRegistryTests()
    {
        CapabilityEndpointBindingRegistry.Reset();
    }

    public void Dispose()
    {
        CapabilityEndpointBindingRegistry.Reset();
    }

    [Fact]
    public void Register_AddsBinding_And_Find_RetrievesIt()
    {
        // Arrange
        var contract = new CapabilityEndpointBindingContract(
            EndpointId: "user.create",
            EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null));

        // Act
        CapabilityEndpointBindingRegistry.Register(contract);
        var found = CapabilityEndpointBindingRegistry.Find("user.create", 1);

        // Assert
        found.Should().NotBeNull();
        found!.EndpointId.Should().Be("user.create");
        found.EndpointVersion.Should().Be(1);
    }

    [Fact]
    public void Register_Duplicate_ThrowsInvalidOperationException()
    {
        // Arrange
        var contract = new CapabilityEndpointBindingContract(
            EndpointId: "user.create",
            EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null));

        CapabilityEndpointBindingRegistry.Register(contract);

        // Act
        var act = () => CapabilityEndpointBindingRegistry.Register(contract);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*user.create*1*");
    }

    [Fact]
    public void Find_NotFound_ReturnsNull()
    {
        // Act
        var found = CapabilityEndpointBindingRegistry.Find("nonexistent", 99);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public void GetRequired_NotFound_ThrowsInvalidOperationException()
    {
        // Act
        var act = () => CapabilityEndpointBindingRegistry.GetRequired("nonexistent", 99);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nonexistent*99*");
    }

    [Fact]
    public void GetRequired_Found_ReturnsContract()
    {
        // Arrange
        var contract = new CapabilityEndpointBindingContract(
            EndpointId: "order.submit",
            EndpointVersion: 2,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null));

        CapabilityEndpointBindingRegistry.Register(contract);

        // Act
        var result = CapabilityEndpointBindingRegistry.GetRequired("order.submit", 2);

        // Assert
        result.Should().BeSameAs(contract);
    }

    [Fact]
    public void Reset_ClearsAllRegistrations()
    {
        // Arrange
        var contract = new CapabilityEndpointBindingContract(
            EndpointId: "user.create",
            EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null));

        CapabilityEndpointBindingRegistry.Register(contract);

        // Act
        CapabilityEndpointBindingRegistry.Reset();

        // Assert
        var found = CapabilityEndpointBindingRegistry.Find("user.create", 1);
        found.Should().BeNull();
    }
}
