using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.DynamicApi;

public static class CapabilityEndpointExtensions
{
    public static IServiceCollection AddCrestCapabilityEndpoints(
        this IServiceCollection services,
        Action<CapabilityEndpointOptions>? configure = null)
    {
        services.TryAddSingleton<ICapabilityEndpointRegistry, CapabilityEndpointRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<CapabilityEndpointDescriptor>,
            RegistryValidationEngine<CapabilityEndpointDescriptor>>();
        services.TryAddSingleton<CapabilityEndpointRegistryBootstrapper>();
        services.TryAddSingleton<CapabilityEndpointHostActivation>();
        services.TryAddSingleton<CapabilityEndpointJsonContractValidator>();
        services.TryAddSingleton<ICapabilityEndpointResultContractRegistry, CapabilityEndpointResultContractRegistry>();

        // Multi-registration interfaces — validators and extractors accumulate across modules.
        // TryAddEnumerable ensures idempotent single registration even if both
        // DynamicApiModule.OnConfigureServices and AddCrestCapabilityEndpoints are called.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IRegistryValidator<CapabilityEndpointDescriptor>,
            CapabilityEndpointDescriptorValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IDescriptorRelationshipExtractor,
            CapabilityEndpointRelationshipExtractor>());

        var options = new CapabilityEndpointOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        return services;
    }

    /// <summary>
    /// Registers one generated assembly contribution for this host. Generated code calls this from
    /// its per-assembly activation extension (for example <c>AddGeneratedCrud()</c>).
    /// </summary>
    /// <remarks>
    /// Registration is idempotent for the same generated contribution type:
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// de-duplicates by implementation type, so repeated generated <c>Add*</c> calls never
    /// double-register a contribution. The instance must therefore be a concrete generated
    /// contribution type (one per assembly), not the base type itself. Contributions are consumed
    /// per host by <see cref="CapabilityEndpointHostActivation"/>; nothing is written to
    /// process-global state.
    /// </remarks>
    public static IServiceCollection AddCrestCapabilityEndpointContribution(
        this IServiceCollection services,
        CapabilityEndpointContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contribution);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<CapabilityEndpointContribution>(contribution));

        return services;
    }

    /// <summary>
    /// Registers compatibility projection services.
    /// Ensures Capability endpoint infrastructure is available.
    /// Callers must also register Capability runtime (e.g., AddCapabilityRuntime())
    /// and ensure compatibility handler invokers are auto-registered via generated [ModuleInitializer].
    /// </summary>
    public static IServiceCollection AddCrestCompatibilityProjection(
        this IServiceCollection services)
    {
        services.AddCrestCapabilityEndpoints();
        return services;
    }

    public static IEndpointRouteBuilder MapCrestCapabilityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var bootstrapper = endpoints.ServiceProvider
            .GetRequiredService<CapabilityEndpointRegistryBootstrapper>();
        bootstrapper.EnsureBuilt();

        var registry = endpoints.ServiceProvider
            .GetRequiredService<ICapabilityEndpointRegistry>();
        var capabilityRegistry = endpoints.ServiceProvider
            .GetRequiredService<ICapabilityRegistry>();
        var resultContractRegistry = endpoints.ServiceProvider
            .GetRequiredService<ICapabilityEndpointResultContractRegistry>();
        var hostActivation = endpoints.ServiceProvider
            .GetRequiredService<CapabilityEndpointHostActivation>();

        // Validate source-generated JSON contract metadata before serving requests
        var contractValidator = endpoints.ServiceProvider
            .GetRequiredService<CapabilityEndpointJsonContractValidator>();
        contractValidator.Validate();

        // Flush pending result contract registrations from legacy generated code into this host's
        // result contract registry; the host activation consults it as the legacy fallback source.
        CapabilityEndpointResultContractRegistration.ApplyTo(resultContractRegistry);

        var resolvedEndpoints = ResolveEndpoints(
            registry, capabilityRegistry, hostActivation, bootstrapper);

        foreach (var item in resolvedEndpoints)
        {
            CapabilityEndpointMapper.MapEndpoint(
                endpoints, item.Descriptor, item.Capability, item.Binding, item.ResultMapper);
            bootstrapper.MarkEndpointMapped(item.Descriptor.Id, item.Descriptor.Version);
        }

        return endpoints;
    }

    /// <summary>
    /// Resolves every active endpoint that still needs mapping on this host. The same
    /// (endpointId, version) identity is resolved at most once across providers and across repeated
    /// map calls; the whole batch is validated before any route is registered so a rejection never
    /// leaves a partially mapped host.
    /// </summary>
    private static List<ResolvedEndpoint> ResolveEndpoints(
        ICapabilityEndpointRegistry registry,
        ICapabilityRegistry capabilityRegistry,
        CapabilityEndpointHostActivation hostActivation,
        CapabilityEndpointRegistryBootstrapper bootstrapper)
    {
        var resolved = new List<ResolvedEndpoint>();
        var seenIdentities = new HashSet<(string EndpointId, int Version)>();

        foreach (var descriptor in registry.GetAll()
            .Where(x => x.State == DescriptorState.Active))
        {
            if (!seenIdentities.Add((descriptor.Id, descriptor.Version)))
            {
                continue;
            }

            if (bootstrapper.IsEndpointMapped(descriptor.Id, descriptor.Version))
            {
                continue;
            }

            var binding = ResolveBinding(descriptor, hostActivation);

            var capability = CapabilityEndpointCapabilityResolver
                .Resolve(capabilityRegistry, descriptor.Capability);

            var resultMapper = ResolveResultMapper(descriptor, hostActivation);

            if (hostActivation.OwnsEndpoint(descriptor.Id, descriptor.Version))
            {
                RequireContributedHandler(descriptor, hostActivation);
            }

            resolved.Add(new ResolvedEndpoint(descriptor, capability, binding, resultMapper));
        }

        return resolved;
    }

    private static CapabilityEndpointBindingContract ResolveBinding(
        CapabilityEndpointDescriptor descriptor,
        CapabilityEndpointHostActivation hostActivation)
    {
        if (hostActivation.OwnsEndpoint(descriptor.Id, descriptor.Version))
        {
            // Owned endpoints must satisfy the explicit contribution contract; legacy static
            // registration visibility is never used for them.
            if (!hostActivation.TryGetContributedBinding(descriptor.Id, descriptor.Version, out var contributed)
                || contributed is null)
            {
                throw new InvalidOperationException(
                    $"Capability endpoint '{descriptor.Id}' v{descriptor.Version} is owned by contribution " +
                    $"'{ResolveOwner(hostActivation, descriptor)}' which provides no input binding contract. " +
                    "ExplicitHost contributions must declare binding, handler and result mapper for every owned endpoint.");
            }

            return contributed;
        }

        return hostActivation.TryGetBinding(descriptor.Id, descriptor.Version)
            ?? throw new InvalidOperationException(
                $"No binding registered for endpoint '{descriptor.Id}' version {descriptor.Version}.");
    }

    private static Func<EndpointExecutionContext, IServiceProvider, object>? ResolveResultMapper(
        CapabilityEndpointDescriptor descriptor,
        CapabilityEndpointHostActivation hostActivation)
    {
        if (hostActivation.OwnsEndpoint(descriptor.Id, descriptor.Version))
        {
            if (!hostActivation.TryGetContributedResultMapper(descriptor.Id, descriptor.Version, out var mapper)
                || mapper is null)
            {
                throw new InvalidOperationException(
                    $"Capability endpoint '{descriptor.Id}' v{descriptor.Version} is owned by contribution " +
                    $"'{ResolveOwner(hostActivation, descriptor)}' which provides no result mapper. A missing result " +
                    "mapper is not downgraded to the generic output fallback.");
            }

            return mapper;
        }

        return hostActivation.TryGetResultMapper(descriptor.Id, descriptor.Version);
    }

    private static void RequireContributedHandler(
        CapabilityEndpointDescriptor descriptor,
        CapabilityEndpointHostActivation hostActivation)
    {
        if (!hostActivation.TryGetContributedHandler(descriptor.Capability.Id, out var invoker)
            || invoker is null)
        {
            throw new InvalidOperationException(
                $"Capability endpoint '{descriptor.Id}' v{descriptor.Version} is owned by contribution " +
                $"'{ResolveOwner(hostActivation, descriptor)}' which provides no handler invoker for capability " +
                $"'{descriptor.Capability.Id}'. ExplicitHost contributions must declare binding, handler and result " +
                "mapper for every owned endpoint.");
        }
    }

    private static string ResolveOwner(
        CapabilityEndpointHostActivation hostActivation,
        CapabilityEndpointDescriptor descriptor)
        => hostActivation.GetEndpointOwner(descriptor.Id, descriptor.Version) ?? "unknown";

    private sealed record ResolvedEndpoint(
        CapabilityEndpointDescriptor Descriptor,
        CapabilityDescriptor Capability,
        CapabilityEndpointBindingContract Binding,
        Func<EndpointExecutionContext, IServiceProvider, object>? ResultMapper);
}
