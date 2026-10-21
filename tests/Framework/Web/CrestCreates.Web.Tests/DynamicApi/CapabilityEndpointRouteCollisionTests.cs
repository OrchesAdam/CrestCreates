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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

/// <summary>
/// #129 S4 exit: the formal endpoint map keeps a batch route collision precheck. Two formal actions
/// with the same method/route are rejected before any endpoint is registered.
/// </summary>
public class CapabilityEndpointRouteCollisionTests
{
    [Fact]
    public void Duplicate_formal_method_route_is_rejected_by_the_registry_before_mapping()
    {
        using var provider = BuildHost(duplicateRoute: true);
        var endpoints = new TestEndpointRouteBuilder(provider);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().Throw<Exception>().WithMessage("*Duplicate capability endpoint route*");
        endpoints.DataSources.SelectMany(source => source.Endpoints).Should().BeEmpty();
    }

    [Fact]
    public void Formal_route_colliding_with_an_already_mapped_endpoint_is_rejected_before_mapping()
    {
        using var provider = BuildHost(duplicateRoute: false);
        var endpoints = new TestEndpointRouteBuilder(provider);
        endpoints.MapMethods("/api/review-first", new[] { "POST" }, _ => Task.CompletedTask);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>().WithMessage("*CCHTTP005*");
        endpoints.DataSources.SelectMany(source => source.Endpoints).Should().HaveCount(1);
    }

    [Fact]
    public void Distinct_formal_method_routes_map_both_endpoints()
    {
        using var provider = BuildHost(duplicateRoute: false);
        var endpoints = new TestEndpointRouteBuilder(provider);

        endpoints.MapCrestCapabilityEndpoints();

        endpoints.DataSources.SelectMany(source => source.Endpoints).Should().HaveCount(2);
    }

    private static ServiceProvider BuildHost(bool duplicateRoute)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICapabilityRegistry>(
            new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>())));
        services.AddSingleton<ICapabilityDispatcher>(
            new Moq.Mock<ICapabilityDispatcher>(Moq.MockBehavior.Strict).Object);
        services.AddCrestCapabilityEndpoints();

        var first = CreateContribution<FirstContribution>("first", duplicateRoute ? "/api/review-collision" : "/api/review-first");
        var second = CreateContribution<SecondContribution>("second", duplicateRoute ? "/api/review-collision" : "/api/review-second");
        services.AddCrestCapabilityEndpointContribution(first);
        services.AddCrestCapabilityEndpointContribution(second);
        return services.BuildServiceProvider();
    }

    private static T CreateContribution<T>(string owner, string route)
        where T : CapabilityEndpointContribution, new()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "review.s4." + owner,
            Name = "Review S4 " + owner,
            Version = 1,
            State = DescriptorState.Active,
        };
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "review.s4.endpoint." + owner,
            Name = "Review S4 endpoint " + owner,
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capability.Id, 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = route,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 },
        };

        return new T
        {
            OwnerAssemblyName = owner,
            CapabilityDescriptors = new[] { capability },
            EndpointDescriptors = new[] { descriptor },
            Bindings = new[]
            {
                new CapabilityEndpointBindingContract(
                    descriptor.Id,
                    1,
                    (_, _) => ValueTask.FromResult<object?>(null)),
            },
            Handlers = new[]
            {
                new CapabilityEndpointHandlerBinding(capability.Id, new EmptyInvoker()),
            },
            ResultMappers = new[]
            {
                new CapabilityEndpointResultBinding(descriptor.Id, 1, (_, _) => Results.NoContent()),
            },
        };
    }

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
