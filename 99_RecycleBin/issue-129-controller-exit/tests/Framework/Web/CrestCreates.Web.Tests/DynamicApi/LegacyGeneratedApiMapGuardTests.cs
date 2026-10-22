using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrestCreates.DynamicApi;
using CrestCreates.Web.Legacy;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

/// <summary>
/// #129 S4 L02 runtime guards: legacy activation is exact-allowlist, and a formal/legacy duplicate
/// method/route is rejected before mapping.
/// </summary>
[Collection("Legacy dynamic api map guard")]
public class LegacyGeneratedApiMapGuardTests
{
    [Fact]
    public void Exact_allowlist_maps_only_the_declared_actions()
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("exact", "/api/exact");

        using var provider = BuildHost(options =>
        {
            options.AddApplicationServiceAssembly<FakeLegacyContract>();
            options.Allow<FakeLegacyContract>("Create", "Get", "Delete");
        });
        var endpoints = new TestEndpointRouteBuilder(provider);

        endpoints.MapLegacyGeneratedApi();

        endpoints.DataSources.SelectMany(source => source.Endpoints).Should().HaveCount(3);
    }

    [Fact]
    public void Missing_action_in_allowlist_fails_before_mapping()
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("missing", "/api/missing");

        using var provider = BuildHost(options =>
        {
            options.AddApplicationServiceAssembly<FakeLegacyContract>();
            options.Allow<FakeLegacyContract>("Create", "Get");
        });
        var endpoints = new TestEndpointRouteBuilder(provider);

        Action map = () => endpoints.MapLegacyGeneratedApi();
        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*CCHTTP007*FakeLegacy.Delete*")
            .WithMessage("*not in the Host allowlist*");
        endpoints.DataSources.Should().BeEmpty();
    }

    [Fact]
    public void Extra_allowlist_action_fails_before_mapping()
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("extra", "/api/extra");

        using var provider = BuildHost(options =>
        {
            options.AddApplicationServiceAssembly<FakeLegacyContract>();
            options.Allow<FakeLegacyContract>("Create", "Get", "Delete", "Extra");
        });
        var endpoints = new TestEndpointRouteBuilder(provider);

        Action map = () => endpoints.MapLegacyGeneratedApi();
        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*CCHTTP007*Extra*")
            .WithMessage("*no generated legacy provider publishes it*");
    }

    [Fact]
    public void Formal_legacy_duplicate_route_is_rejected_before_mapping()
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("duplicate", "/api/duplicate");

        using var provider = BuildHost(options =>
        {
            options.AddApplicationServiceAssembly<FakeLegacyContract>();
            options.Allow<FakeLegacyContract>("Create", "Get", "Delete");
        });
        var endpoints = new TestEndpointRouteBuilder(provider);
        endpoints.MapMethods("/api/duplicate", new[] { "POST" }, _ => Task.CompletedTask);

        Action map = () => endpoints.MapLegacyGeneratedApi();
        map.Should().Throw<InvalidOperationException>().WithMessage("*CCHTTP005*");
    }

    private static ServiceProvider BuildHost(Action<LegacyGeneratedApiOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLegacyGeneratedApi(configure);
        return services.BuildServiceProvider();
    }

    private static void RegisterProvider(string key, string routePrefix)
    {
        DynamicApiGeneratedRegistryStore.Register(new FakeLegacyProvider(key, routePrefix));
    }

    private sealed class FakeLegacyContract;

    private sealed class FakeLegacyProvider(string key, string routePrefix) : IDynamicApiGeneratedProvider
    {
        private string ServiceName => "FakeLegacy";

        public IReadOnlyCollection<Assembly> ServiceAssemblies => new[] { typeof(FakeLegacyContract).Assembly };

        public IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors { get; } = Array.Empty<DynamicApiEndpointDescriptor>();

        public DynamicApiRegistry CreateRegistry(DynamicApiOptions options)
        {
            var actions = new[]
            {
                new DynamicApiActionDescriptor
                {
                    ActionName = "Create",
                    HttpMethod = HttpMethods.Post,
                    RoutePrefix = routePrefix,
                    RelativeRoute = string.Empty,
                },
                new DynamicApiActionDescriptor
                {
                    ActionName = "Get",
                    HttpMethod = HttpMethods.Get,
                    RoutePrefix = routePrefix,
                    RelativeRoute = "{id}",
                },
                new DynamicApiActionDescriptor
                {
                    ActionName = "Delete",
                    HttpMethod = HttpMethods.Delete,
                    RoutePrefix = routePrefix,
                    RelativeRoute = "{id}",
                },
            };

            return new DynamicApiRegistry(new[]
            {
                new DynamicApiServiceDescriptor
                {
                    ServiceName = ServiceName,
                    RoutePrefix = routePrefix,
                    ServiceType = typeof(FakeLegacyContract),
                    ImplementationType = typeof(FakeLegacyContract),
                    Actions = actions,
                }
            });
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
        {
            endpoints.MapMethods(routePrefix, new[] { "POST" }, _ => Task.CompletedTask);
            endpoints.MapMethods(routePrefix + "/{id}", new[] { "GET" }, _ => Task.CompletedTask);
            endpoints.MapMethods(routePrefix + "/{id}", new[] { "DELETE" }, _ => Task.CompletedTask);
        }
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class RegistryStoreSnapshot : IDisposable
    {
        private static readonly FieldInfo ProvidersField =
            typeof(DynamicApiGeneratedRegistryStore).GetField("Providers", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Providers field not found.");

        private readonly (string Key, IDynamicApiGeneratedProvider Provider)[] _providers;

        public RegistryStoreSnapshot()
        {
            var providers = (ConcurrentDictionary<string, IDynamicApiGeneratedProvider>)ProvidersField.GetValue(null)!;
            _providers = providers.Select(pair => (pair.Key, pair.Value)).ToArray();
            providers.Clear();
        }

        public void Dispose()
        {
            var providers = (ConcurrentDictionary<string, IDynamicApiGeneratedProvider>)ProvidersField.GetValue(null)!;
            providers.Clear();
            foreach (var (key, provider) in _providers)
            {
                providers.TryAdd(key, provider);
            }
        }
    }
}
