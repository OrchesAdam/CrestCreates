using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

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
/// </remarks>
internal sealed class CapabilityEndpointRegistryBootstrapper
{
    private readonly ICapabilityEndpointRegistry _registry;
    private readonly CapabilityEndpointRegistry? _retryableRegistry;
    private readonly CapabilityEndpointHostActivation _hostActivation;
    private readonly object _gate = new();
    private readonly HashSet<(string EndpointId, int Version)> _mappedEndpoints = new();
    private bool _ready;

    public CapabilityEndpointRegistryBootstrapper(
        ICapabilityEndpointRegistry registry,
        CapabilityEndpointHostActivation hostActivation)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _retryableRegistry = registry as CapabilityEndpointRegistry;
        _hostActivation = hostActivation ?? throw new ArgumentNullException(nameof(hostActivation));
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

            _ready = true;
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
}
