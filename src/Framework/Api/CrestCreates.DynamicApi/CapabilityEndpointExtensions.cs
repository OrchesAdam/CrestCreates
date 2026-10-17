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

        // §7.2: the capability registry is the authoritative aggregation of the static providers and
        // this host's contributions, and it must be complete *before* the endpoint registry builds —
        // that build validates every endpoint reference against it. A host that already built the
        // registry keeps its build, but a contributed capability it does not know is a deterministic
        // failure rather than a silently unmapped endpoint.
        EnsureCapabilityRegistryCarriesHostContributions(
            endpoints.ServiceProvider.GetRequiredService<ICapabilityRegistry>(),
            endpoints.ServiceProvider.GetRequiredService<CapabilityEndpointHostActivation>());

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

    private static void EnsureCapabilityRegistryCarriesHostContributions(
        ICapabilityRegistry capabilityRegistry,
        CapabilityEndpointHostActivation hostActivation)
    {
        var contributed = hostActivation.GetCapabilityDescriptors();
        if (contributed.Count == 0)
        {
            return;
        }

        if (capabilityRegistry is CapabilityRegistry concrete
            && concrete.State != RegistryState.Built)
        {
            concrete.Build(DescriptorProviderRegistry
                .GetProviders<CapabilityDescriptor>()
                .Concat(new IDescriptorProvider<CapabilityDescriptor>[]
                {
                    new HostCapabilityDescriptorProvider(contributed),
                }));

            foreach (var descriptor in contributed)
            {
                RequireEquivalentContributedCapability(
                    capabilityRegistry,
                    descriptor,
                    "after the aggregation build");
            }

            return;
        }

        foreach (var descriptor in contributed)
        {
            RequireEquivalentContributedCapability(
                capabilityRegistry,
                descriptor,
                "of the host's already-built registry");
        }
    }

    /// <summary>
    /// #128 §7.2: a contributed capability must be the registry's authoritative entry, not merely a
    /// name that happens to exist. The exact declared version is located first (a registry holding
    /// several versions is not a conflict), then version, name, namespace, state, supersession and the
    /// execution semantics (kind, permissions, risk level, projection, input/output schemas — the
    /// Contract-classified fields of the canonical hash profile plus the projection) must match; any
    /// difference is a deterministic pre-mapping rejection naming the capability and owner, and the
    /// frozen registry is never mutated.
    /// </summary>
    private static void RequireEquivalentContributedCapability(
        ICapabilityRegistry capabilityRegistry,
        CapabilityDescriptor contributed,
        string context)
    {
        // Exact-version lookup: GetById is a latest-version index by design and must never stand in
        // for the exact contract the contribution declares.
        var registered = capabilityRegistry.GetByVersion(contributed.Id, contributed.Version);
        if (registered is null)
        {
            var latest = capabilityRegistry.GetById(contributed.Id);
            if (latest is null)
            {
                throw new InvalidOperationException(
                    $"Contributed capability '{contributed.Id}' is missing from the authoritative registry "
                    + context + " (owner '" + contributed.Namespace + "'). The host activation must be registered "
                    + "before the capability registry is built; missing capabilities are never resolved "
                    + "through a generic fallback and never silently skipped.");
            }

            throw new InvalidOperationException(
                $"Contributed capability '{contributed.Id}' v{contributed.Version} (owner '{contributed.Namespace}') "
                + "is missing from the authoritative registry " + context + ": the exact version is not present "
                + $"(the registry holds v{latest.Version} under this id). The frozen registry is never mutated; "
                + "activate the feature against the registry that carries its exact contract.");
        }

        var differences = DescribeCapabilityDifferences(registered, contributed);
        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Contributed capability '{contributed.Id}' v{contributed.Version} (owner '{contributed.Namespace}') "
                + "conflicts with the authoritative registry entry " + context + ": " + string.Join("; ", differences)
                + ". The frozen registry is never mutated, and a feature is never activated against a "
                + "different execution contract; activate the feature before the registry is built.");
        }
    }

    private static List<string> DescribeCapabilityDifferences(
        CapabilityDescriptor registered,
        CapabilityDescriptor contributed)
    {
        var differences = new List<string>();

        if (registered.Version != contributed.Version)
        {
            differences.Add($"version {registered.Version} vs {contributed.Version}");
        }

        if (!string.Equals(registered.Name, contributed.Name, StringComparison.Ordinal))
        {
            differences.Add($"name '{registered.Name}' vs '{contributed.Name}'");
        }

        if (!string.Equals(registered.Namespace, contributed.Namespace, StringComparison.Ordinal))
        {
            differences.Add($"namespace '{registered.Namespace}' vs '{contributed.Namespace}'");
        }

        if (registered.State != contributed.State)
        {
            differences.Add($"state {registered.State} vs {contributed.State}");
        }

        if (!string.Equals(registered.SupersededById, contributed.SupersededById, StringComparison.Ordinal))
        {
            differences.Add($"superseded-by '{registered.SupersededById ?? "<none>"}' vs '{contributed.SupersededById ?? "<none>"}'");
        }

        if (registered.CapabilityKind != contributed.CapabilityKind)
        {
            differences.Add($"kind {registered.CapabilityKind} vs {contributed.CapabilityKind}");
        }

        if (registered.ProjectionKind != contributed.ProjectionKind)
        {
            differences.Add($"projection {registered.ProjectionKind} vs {contributed.ProjectionKind}");
        }

        if (!PermissionsEqual(registered.Permissions, contributed.Permissions))
        {
            differences.Add(
                $"permissions [{string.Join(", ", registered.Permissions)}] vs "
                + $"[{string.Join(", ", contributed.Permissions)}]");
        }

        if (registered.RiskLevel != contributed.RiskLevel)
        {
            differences.Add($"risk level {registered.RiskLevel} vs {contributed.RiskLevel}");
        }

        if (!Equals(registered.InputSchema, contributed.InputSchema))
        {
            differences.Add("input schema reference differs");
        }

        if (!Equals(registered.OutputSchema, contributed.OutputSchema))
        {
            differences.Add("output schema reference differs");
        }

        return differences;
    }

    private static bool PermissionsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => new HashSet<string>(left, StringComparer.Ordinal).SetEquals(right);

    /// <summary>The contributed capability descriptors of one host, exposed as a descriptor provider.</summary>
    private sealed class HostCapabilityDescriptorProvider(IReadOnlyList<CapabilityDescriptor> descriptors)
        : IDescriptorProvider<CapabilityDescriptor>
    {
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => descriptors;
    }

}
