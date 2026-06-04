using CrestCreates.OpenApi;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.OpenApi;

public class CrestOpenApiOptionsTests
{
    [Fact]
    public void DefaultOptions_HasExpectedValues()
    {
        var options = new CrestOpenApiOptions();

        options.EnableOpenApiDocument.Should().BeTrue();
        options.EnableUi.Should().BeTrue();
        options.EnableAuthentication.Should().BeTrue();
        options.EnableTenantHeader.Should().BeTrue();
        options.DocumentTitle.Should().Be("CrestCreates API");
        options.DocumentVersion.Should().Be("v1");
    }

    [Fact]
    public void AddCrestOpenApi_RegistersOptionsAndTransformers()
    {
        var services = new ServiceCollection();

        services.AddCrestOpenApi();

        services.Should().ContainSingle(d => d.ServiceType == typeof(CrestOpenApiOptions));
    }

    [Fact]
    public void AddCrestOpenApi_WithConfiguration_AppliesOptions()
    {
        var services = new ServiceCollection();

        services.AddCrestOpenApi(options =>
        {
            options.DocumentTitle = "Custom API";
            options.EnableUi = false;
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<CrestOpenApiOptions>();

        options.DocumentTitle.Should().Be("Custom API");
        options.EnableUi.Should().BeFalse();
    }
}
