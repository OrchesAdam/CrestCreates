using System.Reflection;
using CrestCreates.DynamicApi;
using CrestCreates.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests;

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
    public void MapCrestWeb_ShouldNotMapOpenIddictEndpoints_WhenDisabled()
    {
        using var snapshot = new DynamicApiRegistryStoreSnapshot();
        DynamicApiGeneratedRegistryStore.Register(new TestDynamicApiProvider());

        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb(options => options.UseOpenIddict(false));

        var app = builder.Build();

        app.MapCrestWeb();

        var routeEndpoints = app.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
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
