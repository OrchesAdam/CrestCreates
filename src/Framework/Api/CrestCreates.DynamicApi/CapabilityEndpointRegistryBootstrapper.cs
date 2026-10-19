using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Per-host bootstrap coordinator for the authoritative <see cref="ICapabilityEndpointRegistry"/>.
/// </summary>
/// <remarks>
/// The bootstrap merges the legacy static provider bag with this host's explicit contributions into a
/// single registry Build. When the registry is already built, the host's required identities and
/// contributed endpoint shapes are verified against the frozen registry instead of mutating it.
/// The ready flag commits only after a successful build and verification, so a failed attempt never
/// permanently poisons the host and a later attempt retries from the then-current inputs. The
/// bootstrapper also owns the per-host mapped-endpoint ledger used to de-duplicate repeated maps by
/// (endpointId, version).
/// The same verification pass also requires every contributed JSON contract root to be answered by the
/// contribution's source-generated context on this host's final <c>Http.Json.JsonOptions</c>; the final
/// options are read at verification time and never cached process-globally.
/// </remarks>
internal sealed class CapabilityEndpointRegistryBootstrapper
{
    private readonly ICapabilityEndpointRegistry _registry;
    private readonly CapabilityEndpointRegistry? _retryableRegistry;
    private readonly CapabilityEndpointHostActivation _hostActivation;
    private readonly IServiceProvider _serviceProvider;
    private readonly object _gate = new();
    private readonly HashSet<(string EndpointId, int Version)> _mappedEndpoints = new();
    private bool _ready;

    public CapabilityEndpointRegistryBootstrapper(
        ICapabilityEndpointRegistry registry,
        CapabilityEndpointHostActivation hostActivation,
        IServiceProvider serviceProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _retryableRegistry = registry as CapabilityEndpointRegistry;
        _hostActivation = hostActivation ?? throw new ArgumentNullException(nameof(hostActivation));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void EnsureBuilt()
    {
        if (Volatile.Read(ref _ready))
        {
            return;
        }

        lock (_gate)
        {
            if (_ready)
            {
                return;
            }

            var providers = new List<IDescriptorProvider<CapabilityEndpointDescriptor>>();
            providers.AddRange(DescriptorProviderRegistry.GetProviders<CapabilityEndpointDescriptor>());

            var contributed = _hostActivation.GetEndpointDescriptors();
            if (contributed.Count > 0)
            {
                providers.Add(new ContributedEndpointDescriptorProvider(contributed));
            }

            if (_retryableRegistry is not null)
            {
                _retryableRegistry.BuildRetryable(providers);
            }
            else
            {
                _registry.Build(providers);
            }

            VerifyHostIdentities();
            VerifyHostJsonContracts();
            VerifyHostRequiredServices();

            _ready = true;
        }
    }

    /// <summary>
    /// CCHTTP007: a contribution's generated invokers resolve the business contract from DI. A host
    /// that activated the contribution but omitted the business service registration must fail
    /// before any endpoint is mapped; the first request must never surface a request-time 500.
    /// </summary>
    private void VerifyHostRequiredServices()
    {
        var entries = _hostActivation.GetRequiredServiceEntries();
        if (entries.Count == 0)
        {
            return;
        }

        var serviceProbe = _serviceProvider.GetService<IServiceProviderIsService>();
        if (serviceProbe is null)
        {
            throw new InvalidOperationException(
                "CCHTTP007: the host's service provider cannot verify business contract registrations "
                + "(IServiceProviderIsService is unavailable); required contracts cannot be validated before mapping.");
        }

        foreach (var entry in entries)
        {
            if (!serviceProbe.IsService(entry.ServiceType))
            {
                throw new InvalidOperationException(
                    $"CCHTTP007: the generated contribution of '{entry.Owner}' requires business contract "
                    + $"service '{entry.ServiceType.FullName}', but the host has no registration for it. "
                    + "Register the business service with its owning feature/module before "
                    + "MapCrestCapabilityEndpoints(); a missing contract registration is never deferred "
                    + "to the first request.");
            }
        }
    }

    /// <summary>
    /// True when this host already mapped the (endpointId, version) identity. Repeat maps are
    /// idempotent; the route table stays stable.
    /// </summary>
    public bool IsEndpointMapped(string endpointId, int version)
    {
        lock (_gate)
        {
            return _mappedEndpoints.Contains((endpointId, version));
        }
    }

    /// <summary>
    /// Records a successfully mapped (endpointId, version) identity for this host.
    /// </summary>
    public void MarkEndpointMapped(string endpointId, int version)
    {
        lock (_gate)
        {
            _mappedEndpoints.Add((endpointId, version));
        }
    }

    /// <summary>
    /// Verifies that every identity this host requires is present in the authoritative registry
    /// and that every contributed endpoint kept its declared shape. Missing identities fail with
    /// owner/action/type diagnostics; they are never resolved through the generic resolver and
    /// never silently skipped.
    /// </summary>
    private void VerifyHostIdentities()
    {
        foreach (var entry in _hostActivation.GetRequiredIdentityEntries())
        {
            var identity = entry.Identity;
            var found = identity.Version > 0
                ? _registry.GetByVersion(identity.IdentityId, identity.Version) is not null
                : _registry.GetById(identity.IdentityId) is not null;

            if (!found)
            {
                throw new InvalidOperationException(
                    $"Required capability endpoint identity '{identity.IdentityId}' is missing from the authoritative registry " +
                    $"(owner '{identity.Owner}', action '{identity.Action}', type '{identity.TypeName}', required by '{entry.RequiredBy}'). " +
                    "The owning contribution must be activated before the registry is built; required identities are never " +
                    "resolved through a generic fallback and never silently skipped.");
            }
        }

        foreach (var descriptor in _hostActivation.GetEndpointDescriptors())
        {
            var owner = _hostActivation.GetEndpointOwner(descriptor.Id, descriptor.Version) ?? "unknown";

            var registered = _registry.GetByVersion(descriptor.Id, descriptor.Version);
            if (registered is null)
            {
                throw new InvalidOperationException(
                    $"Capability endpoint '{descriptor.Id}' v{descriptor.Version} contributed by assembly '{owner}' is missing " +
                    "from the authoritative registry after build. The registry was built without this host contribution; activate " +
                    "the contribution before the registry is built. Contributed endpoints are never silently skipped.");
            }

            if (registered.HttpMethod != descriptor.HttpMethod
                || !string.Equals(registered.RoutePattern, descriptor.RoutePattern, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Capability endpoint '{descriptor.Id}' v{descriptor.Version} contributed by assembly '{owner}' conflicts " +
                    $"with the authoritative registry shape: contributed {descriptor.HttpMethod} '{descriptor.RoutePattern}' " +
                    $"but the registry holds {registered.HttpMethod} '{registered.RoutePattern}'.");
            }
        }
    }

    private sealed class ContributedEndpointDescriptorProvider : IDescriptorProvider<CapabilityEndpointDescriptor>
    {
        private readonly IReadOnlyList<CapabilityEndpointDescriptor> _descriptors;

        public ContributedEndpointDescriptorProvider(IReadOnlyList<CapabilityEndpointDescriptor> descriptors)
        {
            _descriptors = descriptors;
        }

        public IReadOnlyList<CapabilityEndpointDescriptor> GetDescriptors() => _descriptors;
    }

    /// <summary>
    /// #128 §7.2 host-shared JSON roots: framework types every generated CRUD contribution declares
    /// identically (the void/404 envelope and the failure-path error DTO). Their metadata is
    /// framework-owned, so the host's first answerer may be any activated source-generated
    /// contributor of this host; every feature-private root keeps the exact owning-context rule.
    /// </summary>
    private static readonly Type[] HostSharedJsonRootTypes =
    [
        typeof(DynamicApiResponse),
        typeof(ProblemDetails),
    ];

    /// <summary>
    /// #128 §7.1/§7.2 map-time verification: for every contribution and every required JSON contract
    /// root, the contribution's own source-generated context must resolve the type AND the host's final
    /// <c>Http.Json.JsonOptions</c> must resolve it from an activated source-generated contributor of
    /// this host (the owning context for feature-private roots, any of this host's contributed contexts
    /// for the framework-shared void/error roots). The final options are read here, on this host's own
    /// options instance, and never cached process-globally. A reflection resolver never satisfies the
    /// requirement, so a host whose only resolver is <c>DefaultJsonTypeInfoResolver</c> fails even
    /// though reflection could serialize the type.
    /// </summary>
    private void VerifyHostJsonContracts()
    {
        var contracts = _hostActivation.GetContributedJsonContracts();
        if (contracts.Count == 0)
        {
            return;
        }

        var hostContexts = new List<JsonSerializerContext>();
        foreach (var contract in contracts)
        {
            if (contract.JsonContext is not null && !hostContexts.Contains(contract.JsonContext))
            {
                hostContexts.Add(contract.JsonContext);
            }
        }

        JsonSerializerOptions? options = null;
        foreach (var contract in contracts)
        {
            foreach (var required in contract.RequiredJsonRootTypes)
            {
                // Read the final options on first need only, so hosts without JSON contract
                // requirements never depend on Http.Json.JsonOptions being resolvable.
                options ??= _serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

                var reason = DescribeJsonRootFailure(options, contract, required, hostContexts);
                if (reason is not null)
                {
                    throw new InvalidOperationException(
                        $"Required JSON contract root '{required.TypeName}' is not resolved from the declared "
                        + $"source-generated contract (owner '{contract.Owner}', action '{required.Action}', "
                        + $"type '{required.TypeName}', component '{required.Component}'). {reason} "
                        + "CRUD JSON metadata must be supplied by the contribution's source-generated JsonContext "
                        + "on the host's final Http.Json.JsonOptions; it is never resolved through a reflection "
                        + "resolver and never silently skipped.");
                }
            }
        }
    }

    /// <summary>
    /// Returns null when the root is satisfied, otherwise the deterministic reason used by the
    /// diagnostics message. Both halves of §7.2 are checked: the contribution's own closure must carry
    /// the metadata, and the host's final resolver chain must answer it from an activated
    /// source-generated contributor — the owning context for feature-private roots, any contributed
    /// context of this host for the framework-shared void/error roots.
    /// </summary>
    private static string? DescribeJsonRootFailure(
        JsonSerializerOptions options,
        CapabilityEndpointHostActivation.ContributedJsonContract contract,
        CapabilityEndpointJsonRootType required,
        IReadOnlyList<JsonSerializerContext> hostContexts)
    {
        var context = contract.JsonContext;
        if (context is null)
        {
            return "The owning contribution declares no source-generated JsonContext.";
        }

        if (!TryGetContributedMetadata(context, required, out _))
        {
            return "The contribution's source-generated JsonContext does not carry metadata for it.";
        }

        if (!TryFindFirstAnsweringResolver(
                options.TypeInfoResolverChain, required.ClrType, options, out var answering))
        {
            return "The host's final resolver chain answers no metadata for it at all.";
        }

        if (ReferenceEquals(answering, context))
        {
            return null;
        }

        if (answering is JsonSerializerContext answeringContext
            && hostContexts.Contains(answeringContext)
            && Array.IndexOf(HostSharedJsonRootTypes, required.ClrType) >= 0)
        {
            // Framework-shared root: every generated contribution carries identical metadata for it,
            // so any activated source-generated contributor of this host may answer it first.
            return null;
        }

        if (answering is JsonSerializerContext foreignContext
            && hostContexts.Contains(foreignContext))
        {
            return "The host's final resolver chain answers it from another contributed JsonContext "
                + $"('{foreignContext.GetType().FullName}') although this root is feature-private; a private "
                + "root must be answered by its owning contribution's context.";
        }

        return $"The host's final resolver chain answers it from "
            + $"'{answering!.GetType().FullName ?? answering.GetType().Name}' before the contribution's "
            + "JsonContext (activate the generated context after the host's own resolvers).";
    }

    /// <summary>
    /// Asks the contributed context for the root's metadata through the resolver interface, using the
    /// context's own options so the answer is a pure fact about the declared contract.
    /// </summary>
    private static bool TryGetContributedMetadata(
        JsonSerializerContext context,
        CapabilityEndpointJsonRootType required,
        out JsonTypeInfo? metadata)
    {
        try
        {
            metadata = ((IJsonTypeInfoResolver)context).GetTypeInfo(required.ClrType, context.Options);
            return metadata is not null;
        }
        catch (NotSupportedException)
        {
            metadata = null;
            return false;
        }
    }

    /// <summary>
    /// Reproduces the ordered resolution of the final options — the first resolver in the chain that
    /// answers wins — so the verification can tell which resolver actually served the type. A composed
    /// resolver (the chain type <c>JsonTypeInfoResolver.Combine</c> produces) is walked in its own
    /// order at its position, matching how the options themselves resolve the type.
    /// </summary>
    private static bool TryFindFirstAnsweringResolver(
        IList<IJsonTypeInfoResolver> chain,
        Type type,
        JsonSerializerOptions options,
        out IJsonTypeInfoResolver? answering)
    {
        foreach (var resolver in chain)
        {
            if (resolver is not JsonSerializerContext
                && resolver is IList<IJsonTypeInfoResolver> composed)
            {
                if (TryFindFirstAnsweringResolver(composed, type, options, out answering))
                {
                    return true;
                }

                continue;
            }

            try
            {
                if (resolver.GetTypeInfo(type, options) is not null)
                {
                    answering = resolver;
                    return true;
                }
            }
            catch (NotSupportedException)
            {
                // A resolver without metadata for the type reports it this way; the chain continues.
            }
        }

        answering = null;
        return false;
    }
}
