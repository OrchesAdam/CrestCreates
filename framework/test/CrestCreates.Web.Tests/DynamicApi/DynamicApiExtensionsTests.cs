using System.Collections.Generic;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class DynamicApiExtensionsTests
{
    [Fact]
    public void AddCrestDynamicApi_WithoutGeneratedProvider_ThrowsWhenResolvingRegistry()
    {
        var services = new ServiceCollection();

        services.AddCrestDynamicApi(options => options.AddApplicationServiceAssembly(typeof(string).Assembly));

        using var serviceProvider = services.BuildServiceProvider();

        var action = () => serviceProvider.GetRequiredService<DynamicApiRegistry>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*编译期生成的 provider*");
    }

    [Fact]
    public void AddCrestDynamicApi_WithRuntimeFallbackOptIn_CanResolveEmptyRegistryForLegacyDiagnostics()
    {
        var services = new ServiceCollection();

        services.AddCrestDynamicApi(options =>
        {
            options.UseRuntimeReflectionFallback();
            options.AddApplicationServiceAssembly(typeof(string).Assembly);
        });

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<DynamicApiRegistry>();

        registry.Services.Should().BeEmpty();
    }

    [Fact]
    public void MapCrestDynamicApi_WithoutGeneratedProviderAndWithoutFallback_Throws()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddCrestDynamicApi(options => options.AddApplicationServiceAssembly(typeof(string).Assembly));

        using var serviceProvider = services.BuildServiceProvider();
        var endpointRouteBuilder = new DefaultEndpointRouteBuilder(serviceProvider);

        var action = () => endpointRouteBuilder.MapCrestDynamicApi();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*编译期生成的 provider*");
    }

    [Fact]
    public void AddCrestDynamicApi_RegistersSwaggerPostConfigureForGeneratedMainline()
    {
        var services = new ServiceCollection();

        services.AddCrestDynamicApi(options => options.AddApplicationServiceAssembly(typeof(string).Assembly));

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Options.IPostConfigureOptions<Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions>));
    }

    private sealed class DefaultEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public DefaultEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            DataSources = new List<EndpointDataSource>();
        }

        public IServiceProvider ServiceProvider { get; }

        public ICollection<EndpointDataSource> DataSources { get; }

        public IApplicationBuilder CreateApplicationBuilder()
        {
            throw new NotSupportedException();
        }
    }
}
