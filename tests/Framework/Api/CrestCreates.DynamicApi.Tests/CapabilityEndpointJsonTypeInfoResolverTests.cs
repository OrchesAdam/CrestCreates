using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public class CapabilityEndpointJsonTypeInfoResolverTests
{
    [Fact]
    public void Resolve_WithMissingJsonOptions_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var act = () => CapabilityEndpointJsonTypeInfoResolver.Resolve<string>(serviceProvider);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resolve_WithConfiguredJsonOptions_ReturnsJsonTypeInfo()
    {
        // Arrange
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        });
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var result = CapabilityEndpointJsonTypeInfoResolver.Resolve<string>(serviceProvider);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<JsonTypeInfo<string>>();
    }
}
