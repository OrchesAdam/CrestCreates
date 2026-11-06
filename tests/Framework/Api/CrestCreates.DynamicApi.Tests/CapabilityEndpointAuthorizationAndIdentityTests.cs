using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public class CapabilityEndpointAuthorizationAndIdentityTests
{
    [Fact]
    public void Active_required_identity_maps_the_whole_batch()
    {
        var (provider, endpoints) = BuildHost(DescriptorState.Active);

        using (provider)
        {
            endpoints.MapCrestCapabilityEndpoints();

            GetRouteEndpoints(endpoints).Should().HaveCount(2);
        }
    }

    [Theory]
    [InlineData(DescriptorState.Draft)]
    [InlineData(DescriptorState.Deprecated)]
    [InlineData(DescriptorState.Removed)]
    public void Non_publishable_required_identity_fails_before_any_route_is_mapped(DescriptorState state)
    {
        var (provider, endpoints) = BuildHost(state);

        using (provider)
        {
            var act = () => endpoints.MapCrestCapabilityEndpoints();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*required-endpoint*")
                .WithMessage("*not publishable*")
                .WithMessage($"*{state}*");
            GetRouteEndpoints(endpoints).Should().BeEmpty();
        }
    }

    [Fact]
    public void InheritCapability_with_permissions_requires_authentication()
    {
        var capability = CreateCapability("inherit.secured", permissions: ["Books.Create"]);
        var endpoint = CreateEndpoint("inherit.secured.http", capability.Id, "/api/inherit-secured",
            CapabilityEndpointAuthorizationMode.InheritCapability);
        var (provider, endpoints) = BuildHostWithSingleContribution(capability, endpoint);

        using (provider)
        {
            endpoints.MapCrestCapabilityEndpoints();

            var mapped = GetRouteEndpoints(endpoints).Single();
            mapped.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
            mapped.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
                "a permission-bearing capability is never anonymous, even when inherited");
        }
    }

    [Fact]
    public void InheritCapability_without_permissions_keeps_anonymous_semantics()
    {
        var capability = CreateCapability("inherit.open");
        var endpoint = CreateEndpoint("inherit.open.http", capability.Id, "/api/inherit-open",
            CapabilityEndpointAuthorizationMode.InheritCapability);
        var (provider, endpoints) = BuildHostWithSingleContribution(capability, endpoint);

        using (provider)
        {
            endpoints.MapCrestCapabilityEndpoints();

            var mapped = GetRouteEndpoints(endpoints).Single();
            mapped.Metadata.GetMetadata<IAuthorizeData>().Should().BeNull();
            mapped.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        }
    }

    [Fact]
    public void Explicit_allow_anonymous_keeps_the_reachability_marker()
    {
        var capability = CreateCapability("public.open");
        var endpoint = CreateEndpoint("public.open.http", capability.Id, "/api/public-open",
            CapabilityEndpointAuthorizationMode.AllowAnonymous);
        var (provider, endpoints) = BuildHostWithSingleContribution(capability, endpoint);

        using (provider)
        {
            endpoints.MapCrestCapabilityEndpoints();

            var mapped = GetRouteEndpoints(endpoints).Single();
            mapped.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        }
    }

    private static (ServiceProvider Provider, TestEndpointRouteBuilder Endpoints) BuildHost(DescriptorState requiredState)
    {
        var firstCapability = CreateCapability("first.cap");
        var secondCapability = CreateCapability("second.cap");
        var requiredEndpoint = CreateEndpoint("required-endpoint", secondCapability.Id, "/api/required", state: requiredState);
        var ownEndpoint = CreateEndpoint("own-endpoint", firstCapability.Id, "/api/own");

        var services = CreateServices();
        services.AddCrestCapabilityEndpointContribution(new FirstContribution
        {
            OwnerAssemblyName = "first",
            CapabilityDescriptors = [firstCapability],
            EndpointDescriptors = [ownEndpoint],
            Bindings = [CreateBinding(ownEndpoint.Id)],
            Handlers = [new CapabilityEndpointHandlerBinding(firstCapability.Id, new EmptyInvoker())],
            ResultMappers = [new CapabilityEndpointResultBinding(ownEndpoint.Id, 1, (_, _) => Results.NoContent())],
            RequiredIdentityIds = [new CapabilityEndpointRequiredIdentity(requiredEndpoint.Id, "second", "Read", "Thing", 1)]
        });
        services.AddCrestCapabilityEndpointContribution(new SecondContribution
        {
            OwnerAssemblyName = "second",
            CapabilityDescriptors = [secondCapability],
            EndpointDescriptors = [requiredEndpoint],
            Bindings = [CreateBinding(requiredEndpoint.Id)],
            Handlers = [new CapabilityEndpointHandlerBinding(secondCapability.Id, new EmptyInvoker())],
            ResultMappers = [new CapabilityEndpointResultBinding(requiredEndpoint.Id, 1, (_, _) => Results.NoContent())]
        });

        var provider = services.BuildServiceProvider();
        return (provider, new TestEndpointRouteBuilder(provider));
    }

    private static (ServiceProvider Provider, TestEndpointRouteBuilder Endpoints) BuildHostWithSingleContribution(
        CapabilityDescriptor capability,
        CapabilityEndpointDescriptor endpoint)
    {
        var services = CreateServices();
        services.AddCrestCapabilityEndpointContribution(new FirstContribution
        {
            OwnerAssemblyName = "first",
            CapabilityDescriptors = [capability],
            EndpointDescriptors = [endpoint],
            Bindings = [CreateBinding(endpoint.Id)],
            Handlers = [new CapabilityEndpointHandlerBinding(capability.Id, new EmptyInvoker())],
            ResultMappers = [new CapabilityEndpointResultBinding(endpoint.Id, 1, (_, _) => Results.NoContent())]
        });
        var provider = services.BuildServiceProvider();
        return (provider, new TestEndpointRouteBuilder(provider));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICapabilityRegistry>(new CapabilityRegistry(
            new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>())));
        services.AddSingleton(Mock.Of<ICapabilityDispatcher>());
        services.AddCrestCapabilityEndpoints();
        return services;
    }

    private static CapabilityDescriptor CreateCapability(string id, string[]? permissions = null)
        => new()
        {
            Id = id,
            Name = id,
            Version = 1,
            State = DescriptorState.Active,
            Permissions = permissions ?? []
        };

    private static CapabilityEndpointDescriptor CreateEndpoint(
        string id,
        string capabilityId,
        string route,
        CapabilityEndpointAuthorizationMode authorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
        DescriptorState state = DescriptorState.Active)
        => new()
        {
            Id = id,
            Name = id,
            Version = 1,
            State = state,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = route,
            AuthorizationMode = authorizationMode,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

    private static CapabilityEndpointBindingContract CreateBinding(string endpointId)
        => new(endpointId, 1, (_, _) => ValueTask.FromResult<object?>(null));

    private static IReadOnlyList<RouteEndpoint> GetRouteEndpoints(IEndpointRouteBuilder endpoints)
        => endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();

    private sealed class FirstContribution : CapabilityEndpointContribution;

    private sealed class SecondContribution : CapabilityEndpointContribution;

    private sealed class EmptyInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken cancellationToken)
            => Task.FromResult<object?>(null);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
