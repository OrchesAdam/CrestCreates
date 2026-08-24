using System.Reflection;
using System.Threading;
using CrestCreates.DynamicApi;
using CrestCreates.ModuleDiagnostics.Modules;
using CrestCreates.ModuleDiagnostics.Stores;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Accountability.Recording;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Web.Tests;

[Collection("Dynamic API generated registry")]
public class CrestWebPresetTests
{
    [Fact]
    public void CrestWebOptions_ShouldConfigureGeneratedApiAssemblies()
    {
        var options = new CrestWebOptions();

        options.UseGeneratedApi(api => api.AddApplicationServiceAssembly<CrestWebPresetTests>());

        options.GeneratedApi.ServiceMarkerTypes.Should().Contain(typeof(CrestWebPresetTests));
    }

    [Fact]
    public void AddCrestWeb_ShouldAcceptOptionsDelegate()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCrestWeb(options =>
        {
            options.UseGeneratedApi(api => api.AddApplicationServiceAssembly<CrestWebPresetTests>());
        });

        builder.Services.Should().NotBeEmpty();
    }

    [Fact]
    public void AddCrestWeb_ShouldRegisterAccountabilityHttpMainline()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCrestWeb(options => options.UseOpenIddict(false));

        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(AccountabilityHttpTerminalObserverMiddleware));
        builder.Services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(AccountabilityHttpOperationScopeMiddleware));
    }

    [Fact]
    public void ProductionHostCanRequireSinkAfterAddCrestWeb()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));

        builder.Services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<AccountabilityOptions>()
            .RequireAtLeastOneSink.Should().BeTrue();
    }

    [Fact]
    public void CrestWebAccountabilityConfigurationIsNotFirstCallWins()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));
        builder.Services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        builder.Services.AddAccountability(options => options.RequireAtLeastOneSink = false);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<AccountabilityOptions>()
            .RequireAtLeastOneSink.Should().BeFalse();
    }

    [Fact]
    public async Task ProductionHostWithoutRequiredSinkFailsStartup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));
        builder.Services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        using var provider = builder.Services.BuildServiceProvider();
        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "AccountabilityCompositionValidator");

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ACCOUNTABILITY_SINK_REQUIRED*");
    }

    [Fact]
    public async Task DevelopmentHostExplicitlyRegistersInMemorySink()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));
        builder.Services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        builder.Services.AddSingleton<IAuditSink>(new InMemoryAuditSink());
        using var provider = builder.Services.BuildServiceProvider();
        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "AccountabilityCompositionValidator");

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public void MapCrestWeb_ShouldNotMapOpenIddictEndpoints_WhenDisabled()
    {
        using var snapshot = new DynamicApiRegistryStoreSnapshot();
        DynamicApiGeneratedRegistryStore.Register(new TestDynamicApiProvider());

        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));

        var app = builder.Build();

        app.MapCrestWeb();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .ToArray();

        routeEndpoints.Should().NotContain("/connect/authorize");
        routeEndpoints.Should().NotContain("/connect/token");
        routeEndpoints.Should().NotContain("/connect/userinfo");
        routeEndpoints.Should().NotContain("/connect/logout");
    }

    [Fact]
    public void MapCrestWeb_ShouldMapHealthEndpoint()
    {
        using var snapshot = new DynamicApiRegistryStoreSnapshot();
        DynamicApiGeneratedRegistryStore.Register(new TestDynamicApiProvider());

        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));

        var app = builder.Build();

        app.MapCrestWeb();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.RoutePattern.RawText))
            .ToArray();

        routeEndpoints.Select(endpoint => endpoint.RoutePattern.RawText).Should().Contain("/health");
        routeEndpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/health")
            .Metadata
            .GetMetadata<SkipTenantResolutionMetadata>()
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void AddCrestWeb_ShouldRegisterModuleDiagnostics()
    {
        using var snapshot = new DynamicApiRegistryStoreSnapshot();
        DynamicApiGeneratedRegistryStore.Register(new TestDynamicApiProvider());

        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));

        using var serviceProvider = builder.Services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IModuleDiagnosticsStore>()
            .Should()
            .BeSameAs(ModuleDiagnosticsServiceCollectionExtensions.Store);

        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        registrations.Select(registration => registration.Name).Should().Contain("modules");
    }

    [Fact]
    public void InitializeCrestAsync_ShouldBeExposedOnWebApplication()
    {
        var method = typeof(CrestCreatesWebApplicationExtensions)
            .GetMethods()
            .SingleOrDefault(method => method.Name == "InitializeCrestAsync");

        method.Should().NotBeNull();
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
            ((ICollection<KeyValuePair<string, IDynamicApiGeneratedProvider>>)ProvidersField.GetValue(null)!).Clear();
        }

        public void Dispose()
        {
            var providers = (ICollection<KeyValuePair<string, IDynamicApiGeneratedProvider>>)ProvidersField.GetValue(null)!;
            providers.Clear();
            foreach (var (key, provider) in _providers)
            {
                providers.Add(new KeyValuePair<string, IDynamicApiGeneratedProvider>(key, provider));
            }
        }
    }

    private sealed class TestDynamicApiProvider : IDynamicApiGeneratedProvider
    {
        public IReadOnlyCollection<System.Reflection.Assembly> ServiceAssemblies => new[] { typeof(CrestWebPresetTests).Assembly };

        public IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors { get; } =
            new[]
            {
                new DynamicApiEndpointDescriptor(
                    "Test",
                    "Ping",
                    "GET",
                    "test/ping",
                    typeof(CrestWebPresetTests),
                    null,
                    typeof(string),
                    Array.Empty<string>(),
                    false)
            };

        public DynamicApiRegistry CreateRegistry(DynamicApiOptions options)
        {
            return new DynamicApiRegistry(new[]
            {
                new DynamicApiServiceDescriptor
                {
                    ServiceName = "Test",
                    RoutePrefix = "test",
                    ServiceType = typeof(CrestWebPresetTests),
                    ImplementationType = typeof(CrestWebPresetTests),
                    Actions = Array.Empty<DynamicApiActionDescriptor>()
                }
            });
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
        {
        }
    }
}
