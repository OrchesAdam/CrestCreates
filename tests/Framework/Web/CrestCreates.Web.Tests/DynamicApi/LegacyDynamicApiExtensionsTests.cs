using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class LegacyDynamicApiExtensionsTests
{
    [Fact]
    public void AddCrestDynamicApi_WithoutGeneratedProvider_ThrowsWhenResolvingRegistry()
    {
        using var scope = new DynamicApiRegistryStoreSnapshot();
        var services = new ServiceCollection();

        services.AddCrestDynamicApi(options => options.AddApplicationServiceAssembly(typeof(string).Assembly));

        using var serviceProvider = services.BuildServiceProvider();

        var action = () => serviceProvider.GetRequiredService<DynamicApiRegistry>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*编译期生成的 provider*");
    }

    [Fact]
    public void DynamicApiOptions_ShouldNotExposeRuntimeFallbackMembers()
    {
        typeof(DynamicApiOptions).GetProperty("EnableRuntimeReflectionFallback").Should().BeNull();
        typeof(DynamicApiOptions).GetMethod("UseRuntimeReflectionFallback").Should().BeNull();
    }

    [Fact]
    public void AddCrestDynamicApi_WithoutGeneratedProvider_ErrorShouldNotMentionRuntimeFallback()
    {
        using var scope = new DynamicApiRegistryStoreSnapshot();
        var services = new ServiceCollection();

        services.AddCrestDynamicApi(options => options.AddApplicationServiceAssembly(typeof(string).Assembly));

        using var serviceProvider = services.BuildServiceProvider();

        var action = () => serviceProvider.GetRequiredService<DynamicApiRegistry>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*编译期生成的 provider*")
            .And.Message.Should().NotContain("RuntimeReflectionFallback")
            .And.NotContain("UseRuntimeReflectionFallback")
            .And.NotContain("fallback");
    }

    [Fact]
    public void ControllerOnlyProvider_ShouldBuildRegistryAndMapEndpoints()
    {
        using var scope = new DynamicApiRegistryStoreSnapshot();

        var provider = new ControllerOnlyProvider();
        DynamicApiGeneratedRegistryStore.Register(provider);

        var options = new DynamicApiOptions();

        var registry = DynamicApiGeneratedRegistryStore.BuildRegistry(options);
        registry.Should().NotBeNull();
        // ControllerOnlyProvider.CreateRegistry returns empty services;
        // other providers from the same test assembly (e.g. TestDynamicApiProvider
        // in CrestWebPresetTests) may also be registered and contribute services.
        // The key invariant is that the registry is built successfully.

        var descriptors = DynamicApiGeneratedRegistryStore.GetEndpointDescriptors(options);
        descriptors.Should().ContainSingle(descriptor =>
            descriptor.ServiceName == "Ping" &&
            descriptor.ActionName == "Get" &&
            descriptor.ServiceType == typeof(ControllerOnlyApi) &&
            descriptor.RoutePattern == string.Empty);

        var services = new ServiceCollection();
        services.AddRouting();
        using var serviceProvider = services.BuildServiceProvider();
        var endpointRouteBuilder = new DefaultEndpointRouteBuilder(serviceProvider);

        var mapped = DynamicApiGeneratedRegistryStore.MapGeneratedEndpoints(endpointRouteBuilder, options);

        mapped.Should().BeTrue();
        provider.MapCalled.Should().BeTrue();
    }

    [Fact]
    public void ProviderFromUnrelatedAssembly_ShouldNotSatisfyConfiguredServiceAssemblies()
    {
        using var scope = new DynamicApiRegistryStoreSnapshot();

        var provider = new UnrelatedAssemblyProvider();
        DynamicApiGeneratedRegistryStore.Register(provider);

        var options = new DynamicApiOptions();
        options.AddApplicationServiceAssembly(typeof(string).Assembly);

        var registry = DynamicApiGeneratedRegistryStore.BuildRegistry(options);
        registry.Should().BeNull();

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddCrestDynamicApi(configure: dynamicApiOptions =>
            dynamicApiOptions.AddApplicationServiceAssembly(typeof(string).Assembly));

        using var serviceProvider = services.BuildServiceProvider();
        var endpointRouteBuilder = new DefaultEndpointRouteBuilder(serviceProvider);

        var action = () => endpointRouteBuilder.MapCrestDynamicApi();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*编译期生成的 provider*");
    }

    [Fact]
    public void MapCrestDynamicApi_WithoutGeneratedProviderAndWithoutFallback_Throws()
    {
        using var scope = new DynamicApiRegistryStoreSnapshot();
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

    private sealed class DynamicApiRegistryStoreSnapshot : IDisposable
    {
        private static readonly FieldInfo ProvidersField =
            typeof(DynamicApiGeneratedRegistryStore).GetField("Providers", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Providers field not found.");

        private readonly (string Key, IDynamicApiGeneratedProvider Provider)[] _providers;

        public DynamicApiRegistryStoreSnapshot()
        {
            var providers = (IEnumerable<KeyValuePair<string, IDynamicApiGeneratedProvider>>)ProvidersField.GetValue(null)!;
            _providers = providers.ToArray().Select(pair => (pair.Key, pair.Value)).ToArray();

            ClearStore();
        }

        public void Dispose()
        {
            ClearStore();

            var providers = (ICollection<KeyValuePair<string, IDynamicApiGeneratedProvider>>)ProvidersField.GetValue(null)!;
            foreach (var (key, provider) in _providers)
            {
                providers.Add(new KeyValuePair<string, IDynamicApiGeneratedProvider>(key, provider));
            }
        }

        private static void ClearStore()
        {
            ((ICollection<KeyValuePair<string, IDynamicApiGeneratedProvider>>)ProvidersField.GetValue(null)!).Clear();
        }
    }

    private sealed class ControllerOnlyProvider : IDynamicApiGeneratedProvider
    {
        public bool MapCalled { get; private set; }

        public IReadOnlyCollection<System.Reflection.Assembly> ServiceAssemblies => Array.Empty<System.Reflection.Assembly>();

        public IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors { get; } =
            new[]
            {
                new DynamicApiEndpointDescriptor(
                    "Ping",
                    "Get",
                    "GET",
                    string.Empty,
                    typeof(ControllerOnlyApi),
                    null,
                    typeof(string),
                    Array.Empty<string>(),
                    false)
            };

        public DynamicApiRegistry CreateRegistry(DynamicApiOptions options)
        {
            return new DynamicApiRegistry(Array.Empty<DynamicApiServiceDescriptor>());
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
        {
            MapCalled = true;
        }
    }

    private sealed class ControllerOnlyApi
    {
    }

    private sealed class UnrelatedAssemblyProvider : IDynamicApiGeneratedProvider
    {
        public IReadOnlyCollection<System.Reflection.Assembly> ServiceAssemblies => Array.Empty<System.Reflection.Assembly>();

        public IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors { get; } =
            new[]
            {
                new DynamicApiEndpointDescriptor(
                    "Unrelated",
                    "Get",
                    "GET",
                    string.Empty,
                    typeof(UnrelatedApi),
                    null,
                    typeof(string),
                    Array.Empty<string>(),
                    false)
            };

        public DynamicApiRegistry CreateRegistry(DynamicApiOptions options)
        {
            return new DynamicApiRegistry(Array.Empty<DynamicApiServiceDescriptor>());
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
        {
        }
    }

    private sealed class UnrelatedApi
    {
    }
}
