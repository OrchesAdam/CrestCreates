using System.Threading;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing.Matching;
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
public class ReviewS4LegacyIsolationProbes
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

    [Fact]
    public void ReviewS4_old_public_map_cannot_bypass_allowlist()
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("bypass", "/api/review-bypass");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestDynamicApi(options => options.AddApplicationServiceAssembly(typeof(FakeLegacyContract).Assembly));
        using var provider = services.BuildServiceProvider();
        var endpoints = new TestEndpointRouteBuilder(provider);
        var rejected = Record.Exception(() => endpoints.MapCrestDynamicApi());
        var count = endpoints.DataSources.SelectMany(source => source.Endpoints).Count();
        rejected.Should().BeOfType<InvalidOperationException>($"no allowlist was configured; mapped endpoints={count}")
            .Which.Message.Should().Contain("CCHTTP007");
        count.Should().Be(0);
    }

    [Theory]
    [InlineData("/api/review-collision", "POST", true)]
    [InlineData("/API/REVIEW-COLLISION", "POST", true)]
    [InlineData("/api/review-collision/{ticketId}", "GET", true)]
    [InlineData("/api/review-collision", "GET", false)]
    [InlineData("/api/other", "POST", false)]
    public void ReviewS4_collision_guard_uses_route_matching_semantics(string existingRoute, string method, bool collision)
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("collision", "/api/review-collision");
        using var provider = BuildHost(options =>
        {
            options.AddApplicationServiceAssembly<FakeLegacyContract>();
            options.Allow<FakeLegacyContract>("Create", "Get", "Delete");
        });
        var endpoints = new TestEndpointRouteBuilder(provider);
        endpoints.MapMethods(existingRoute, new[] { method }, _ => Task.CompletedTask);
        var rejected = Record.Exception(() => endpoints.MapLegacyGeneratedApi());
        var count = endpoints.DataSources.SelectMany(source => source.Endpoints).Count();
        if (collision)
        {
            rejected.Should().BeOfType<InvalidOperationException>($"conflicting route must be rejected; routes after map={count}")
                .Which.Message.Should().Contain("CCHTTP005");
            count.Should().Be(1);
        }
        else
        {
            rejected.Should().BeNull();
            count.Should().Be(4);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviewS4_formal_legacy_conflict_is_independent_of_map_order(bool formalFirst)
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("order", "/api/review-order");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLegacyGeneratedApi(options =>
        {
            options.AddApplicationServiceAssembly<FakeLegacyContract>();
            options.Allow<FakeLegacyContract>("Create", "Get", "Delete");
        });
        var capability = new CapabilityDescriptor { Id = "review.s4.cap", Name = "Review S4", Version = 1, State = DescriptorState.Active };
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "review.s4.endpoint", Name = "Review S4 endpoint", Version = 1, State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capability.Id, 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post, RoutePattern = "/api/review-order",
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };
        var registry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityDispatcher>(new Moq.Mock<ICapabilityDispatcher>(Moq.MockBehavior.Strict).Object);
        services.AddCrestCapabilityEndpoints();
        services.AddCrestCapabilityEndpointContribution(new ReviewS4Contribution
        {
            OwnerAssemblyName = "ReviewS4", CapabilityDescriptors = new[] { capability }, EndpointDescriptors = new[] { descriptor },
            Bindings = new[] { new CapabilityEndpointBindingContract(descriptor.Id, 1, (_, _) => ValueTask.FromResult<object?>(null)) },
            Handlers = new[] { new CapabilityEndpointHandlerBinding(capability.Id, new EmptyInvoker()) },
            ResultMappers = new[] { new CapabilityEndpointResultBinding(descriptor.Id, 1, (_, _) => Results.NoContent()) }
        });
        using var provider = services.BuildServiceProvider();
        var endpoints = new TestEndpointRouteBuilder(provider);
        if (formalFirst) endpoints.MapCrestCapabilityEndpoints();
        else endpoints.MapLegacyGeneratedApi();
        var before = endpoints.DataSources.SelectMany(source => source.Endpoints).Count();
        before.Should().Be(formalFirst ? 1 : 3, "the first map must be a valid positive control");
        var rejected = Record.Exception(() =>
        {
            if (formalFirst) endpoints.MapLegacyGeneratedApi();
            else endpoints.MapCrestCapabilityEndpoints();
        });
        var after = endpoints.DataSources.SelectMany(source => source.Endpoints).Count();
        rejected.Should().BeOfType<InvalidOperationException>($"both orders must reject double publish; endpoints before={before}, after={after}")
            .Which.Message.Should().Contain("CCHTTP005");
        after.Should().Be(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewS4_actual_router_confirms_semantic_collision(bool renameParameter)
    {
        using var store = new RegistryStoreSnapshot();
        RegisterProvider("router", "/api/review-router");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLegacyGeneratedApi(options =>
        {
            options.AddApplicationServiceAssembly<FakeLegacyContract>();
            options.Allow<FakeLegacyContract>("Create", "Get", "Delete");
        });
        await using var app = builder.Build();
        app.MapMethods(renameParameter ? "/api/review-router/{ticketId}" : "/API/REVIEW-ROUTER",
            new[] { renameParameter ? "GET" : "POST" }, _ => Task.CompletedTask);
        var rejected = Record.Exception(() => app.MapLegacyGeneratedApi());
        if (rejected is not null)
        {
            rejected.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("CCHTTP005");
            return;
        }
        await app.StartAsync();
        using var client = app.GetTestClient();
        var failure = await Record.ExceptionAsync(async () =>
        {
            using var response = renameParameter
                ? await client.GetAsync("/api/review-router/42")
                : await client.PostAsync("/api/review-router", null);
        });
        failure.Should().NotBeNull("the official router must detect the ambiguous patterns");
        failure!.GetType().FullName.Should().Be("Microsoft.AspNetCore.Routing.Matching.AmbiguousMatchException");
        failure.GetType().Assembly.GetName().Name.Should().Be("Microsoft.AspNetCore.Routing");
    }

    private sealed class ReviewS4Contribution : CapabilityEndpointContribution;
    private sealed class EmptyInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct) => Task.FromResult<object?>(null);
    }

    [Fact]
    public void ReviewS4_allowlist_preserves_contract_assembly_identity()
    {
        using var store = new RegistryStoreSnapshot();
        var contractA = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("ReviewS4OwnerA"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
            .DefineDynamicModule("OwnerA").DefineType("Review.Shared.Service", TypeAttributes.Public)
            .CreateTypeInfo()!.AsType();
        var contractB = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("ReviewS4OwnerB"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
            .DefineDynamicModule("OwnerB").DefineType("Review.Shared.Service", TypeAttributes.Public)
            .CreateTypeInfo()!.AsType();
        contractA.FullName.Should().Be(contractB.FullName);
        contractA.Should().NotBe(contractB);
        contractA.Assembly.Should().NotBeSameAs(contractB.Assembly);
        DynamicApiGeneratedRegistryStore.Register(new OtherOwnerProvider(contractB));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLegacyGeneratedApi(options =>
        {
            var select = typeof(LegacyGeneratedApiOptions).GetMethod(nameof(LegacyGeneratedApiOptions.AddApplicationServiceAssembly))!;
            select.MakeGenericMethod(contractA).Invoke(options, null);
            select.MakeGenericMethod(contractB).Invoke(options, null);
            typeof(LegacyGeneratedApiOptions).GetMethod(nameof(LegacyGeneratedApiOptions.Allow))!
                .MakeGenericMethod(contractA).Invoke(options, new object[] { new[] { "Get" } });
        });
        using var provider = services.BuildServiceProvider();
        var endpoints = new TestEndpointRouteBuilder(provider);
        var rejected = Record.Exception(() => endpoints.MapLegacyGeneratedApi());
        var count = endpoints.DataSources.SelectMany(source => source.Endpoints).Count();
        rejected.Should().BeOfType<InvalidOperationException>($"allowing OwnerA must not allow the different OwnerB type; mapped={count}")
            .Which.Message.Should().Contain("CCHTTP007");
        count.Should().Be(0);
    }

    private sealed class OtherOwnerProvider(Type contract) : IDynamicApiGeneratedProvider
    {
        public IReadOnlyCollection<Assembly> ServiceAssemblies => new[] { contract.Assembly };
        public IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors => Array.Empty<DynamicApiEndpointDescriptor>();
        public DynamicApiRegistry CreateRegistry(DynamicApiOptions options) => new(new[]
        {
            new DynamicApiServiceDescriptor
            {
                ServiceName = "OtherOwner", ServiceType = contract, ImplementationType = contract,
                RoutePrefix = "/api/other-owner", Actions = new[]
                {
                    new DynamicApiActionDescriptor { ActionName = "Get", HttpMethod = "GET", RoutePrefix = "/api/other-owner", RelativeRoute = "" }
                }
            }
        });
        public void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
            => endpoints.MapMethods("/api/other-owner", new[] { "GET" }, _ => Task.CompletedTask);
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
