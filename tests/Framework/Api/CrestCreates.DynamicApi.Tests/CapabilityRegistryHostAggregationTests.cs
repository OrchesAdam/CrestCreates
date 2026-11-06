#nullable enable
using System;
using System.Collections.Generic;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

/// <summary>
/// #128 S3 (G1, negative direction): a host that built the authoritative capability registry itself
/// must carry its contributions; when it does not, mapping fails deterministically with the missing
/// capability identity instead of silently leaving the endpoints unmapped. The positive direction —
/// Map performing the single authoritative aggregation — is proven by the real HTTP fixture, which no
/// longer builds the registry by hand.
/// </summary>
public class CapabilityRegistryHostAggregationTests
{
    [Fact]
    public void Host_that_prebuilt_the_registry_without_the_contribution_must_fail_map_naming_the_capability()
    {
        const string capabilityId = "WidgetFeature.WidgetAppService.Create";
        var registry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>(
            Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestCapabilityEndpoints();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddCrestCapabilityEndpointContribution(new ProbeContribution
        {
            OwnerAssemblyName = "WidgetFeature",
            CapabilityDescriptors =
            [
                new CapabilityDescriptor { Id = capabilityId, Name = "Create", Version = 1 },
            ],
        });

        // The host activated the contribution but built the registry before it could join: the
        // authoritative registry now lacks the capability the contribution owns.
        registry.Build(Array.Empty<IDescriptorProvider<CapabilityDescriptor>>());
        registry.State.Should().Be(RegistryState.Built);

        using var provider = services.BuildServiceProvider();
        var endpoints = new MinimalEndpointRouteBuilder(provider);

        var act = () => endpoints.MapCrestCapabilityEndpoints();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*" + capabilityId + "*",
                "the failure must name the capability missing from the authoritative registry");
    }

    [Fact]
    public void BuildFromHostCatalog_composes_host_catalog_and_activated_contributions()
    {
        var registry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>(
            Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestCapabilityEndpoints();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddCrestCapabilityEndpointContribution(new ProbeContribution
        {
            OwnerAssemblyName = "WidgetFeature",
            CapabilityDescriptors =
            [
                new CapabilityDescriptor { Id = "WidgetFeature.WidgetAppService.Create", Name = "Create", Version = 1 },
            ],
        });

        using var provider = services.BuildServiceProvider();
        provider.BuildFromHostCatalog(
        [
            new CapabilityDescriptor { Id = "AssetManagement.Asset", Name = "AssetCapability", Version = 1 },
        ]);

        registry.State.Should().Be(RegistryState.Built);
        registry.GetByNameAndVersion("AssetCapability", 1).Should().NotBeNull();
        registry.GetByNameAndVersion("Create", 1).Should().NotBeNull();
    }

    [Fact]
    public void BuildFromHostCatalog_fails_closed_when_the_registry_was_built_without_the_host_catalog()
    {
        var registry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>(
            Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestCapabilityEndpoints();
        services.AddSingleton<ICapabilityRegistry>(registry);

        using var provider = services.BuildServiceProvider();

        // Another bootstrap built the registry first without the host's static catalog.
        registry.Build(Array.Empty<IDescriptorProvider<CapabilityDescriptor>>());
        registry.State.Should().Be(RegistryState.Built);

        var act = () => provider.BuildFromHostCatalog(
        [
            new CapabilityDescriptor { Id = "AssetManagement.Asset", Name = "AssetCapability", Version = 1 },
        ]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AssetCapability*",
                "a registry that omits the host catalog must not be trusted");
    }

    /// <summary>Generated contributions are concrete per-assembly types; TryAddEnumerable requires it.</summary>
    private sealed class ProbeContribution : CapabilityEndpointContribution
    {
    }

    private sealed class MinimalEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
