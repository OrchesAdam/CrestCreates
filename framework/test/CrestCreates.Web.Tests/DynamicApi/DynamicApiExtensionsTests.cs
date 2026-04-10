using System.Collections.Generic;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    public void AddCrestDynamicApi_WithRuntimeFallbackOptIn_RegistersLegacyScannerPath()
    {
        var services = new ServiceCollection();

        services.AddCrestDynamicApi(options => options.UseRuntimeReflectionFallback());

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IDynamicApiScanner) &&
            descriptor.ImplementationType == typeof(DynamicApiScanner));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(DynamicApiEndpointExecutor) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
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
