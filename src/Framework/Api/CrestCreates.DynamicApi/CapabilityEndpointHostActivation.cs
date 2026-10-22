using System.Collections.Frozen;
using System.Text.Json.Serialization;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Per-host aggregation of the CapabilityEndpoint contributions explicitly activated on this host.
/// </summary>
/// <remarks>
/// The activation is the host-authoritative answer source for endpoint descriptors, bindings,
/// handlers, result mappers, body types, required identities and the per-contribution JSON contract
/// requirements. Lookups never fall back to another host: the legacy static registries remain a
/// fallback source only so existing compatibility consumers keep working, and they are consulted
/// after the host contribution.
/// </remarks>
public sealed class CapabilityEndpointHostActivation
{
    private readonly ICapabilityEndpointResultContractRegistry _legacyResultContracts;
    private readonly FrozenDictionary<(string EndpointId, int Version), (CapabilityEndpointBindingContract Binding, string Owner)> _contributedBindings;
    private readonly FrozenDictionary<string, (ICapabilityHandlerInvoker Invoker, string Owner)> _contributedHandlers;
    private readonly FrozenDictionary<(string EndpointId, int Version), (Func<EndpointExecutionContext, IServiceProvider, object> Mapper, string Owner)> _contributedResultMappers;
    private readonly FrozenDictionary<(string EndpointId, int Version), (CapabilityEndpointDescriptor Descriptor, string Owner)> _contributedEndpoints;
    private readonly IReadOnlyList<CapabilityEndpointDescriptor> _endpointDescriptors;
    private readonly IReadOnlyList<CapabilityDescriptor> _capabilityDescriptors;
    private readonly IReadOnlyList<Type> _bodyTypes;
    private readonly IReadOnlyList<CapabilityEndpointRequiredIdentity> _requiredIdentities;
    private readonly IReadOnlyList<RequiredIdentityEntry> _requiredIdentityEntries;
    private readonly IReadOnlyList<RequiredServiceEntry> _requiredServiceEntries;
    private readonly IReadOnlyList<ContributedJsonContract> _contributedJsonContracts;

    public CapabilityEndpointHostActivation(
        IEnumerable<CapabilityEndpointContribution> contributions,
        ICapabilityEndpointResultContractRegistry legacyResultContracts)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(legacyResultContracts);

        _legacyResultContracts = legacyResultContracts;

        var bindings = new Dictionary<(string, int), (CapabilityEndpointBindingContract, string)>();
        var handlers = new Dictionary<string, (ICapabilityHandlerInvoker, string)>(StringComparer.Ordinal);
        var resultMappers = new Dictionary<(string, int), (Func<EndpointExecutionContext, IServiceProvider, object>, string)>();
        var endpoints = new Dictionary<(string, int), (CapabilityEndpointDescriptor, string)>();
        var endpointDescriptors = new List<CapabilityEndpointDescriptor>();
        var capabilityDescriptors = new List<CapabilityDescriptor>();
        var bodyTypes = new List<Type>();
        var requiredIdentities = new List<CapabilityEndpointRequiredIdentity>();
        var requiredIdentityEntries = new List<RequiredIdentityEntry>();
        var requiredServiceEntries = new List<RequiredServiceEntry>();
        var contributedJsonContracts = new List<ContributedJsonContract>();

        foreach (var contribution in contributions)
        {
            ArgumentNullException.ThrowIfNull(contribution);

            if (string.IsNullOrWhiteSpace(contribution.OwnerAssemblyName))
            {
                throw new InvalidOperationException(
                    $"Capability endpoint contribution '{contribution.GetType().FullName}' must declare OwnerAssemblyName for per-host diagnostics.");
            }

            var owner = contribution.OwnerAssemblyName;

            foreach (var binding in contribution.Bindings)
            {
                var key = (binding.EndpointId, binding.EndpointVersion);
                if (bindings.TryGetValue(key, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate capability endpoint binding contribution for endpoint '{key.EndpointId}' version {key.EndpointVersion} between assemblies '{existing.Item2}' and '{owner}'.");
                }

                bindings.Add(key, (binding, owner));
            }

            foreach (var handler in contribution.Handlers)
            {
                if (handlers.TryGetValue(handler.CapabilityId, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate capability endpoint handler contribution for capability '{handler.CapabilityId}' between assemblies '{existing.Item2}' and '{owner}'.");
                }

                handlers.Add(handler.CapabilityId, (handler.Invoker, owner));
            }

            foreach (var resultMapper in contribution.ResultMappers)
            {
                var key = (resultMapper.EndpointId, resultMapper.EndpointVersion);
                if (resultMappers.TryGetValue(key, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate capability endpoint result mapper contribution for endpoint '{key.EndpointId}' version {key.EndpointVersion} between assemblies '{existing.Item2}' and '{owner}'.");
                }

                resultMappers.Add(key, (resultMapper.MapResult, owner));
            }

            foreach (var descriptor in contribution.EndpointDescriptors)
            {
                var key = (descriptor.Id, descriptor.Version);
                if (endpoints.TryGetValue(key, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate capability endpoint descriptor contribution for endpoint '{key.Id}' version {key.Version} between assemblies '{existing.Item2}' and '{owner}'.");
                }

                endpoints.Add(key, (descriptor, owner));
                endpointDescriptors.Add(descriptor);
            }

            capabilityDescriptors.AddRange(contribution.CapabilityDescriptors);

            foreach (var bodyType in contribution.BodyTypes)
            {
                if (!bodyTypes.Contains(bodyType))
                {
                    bodyTypes.Add(bodyType);
                }
            }

            foreach (var identity in contribution.RequiredIdentityIds)
            {
                var effective = string.IsNullOrWhiteSpace(identity.Owner)
                    ? identity with { Owner = owner }
                    : identity;
                requiredIdentities.Add(effective);
                requiredIdentityEntries.Add(new RequiredIdentityEntry(effective, owner));
            }

            foreach (var serviceType in contribution.RequiredServiceTypes)
            {
                ArgumentNullException.ThrowIfNull(serviceType);
                requiredServiceEntries.Add(new RequiredServiceEntry(owner, serviceType));
            }

            // §7.2: the JSON contract requirements of this contribution travel with its owner identity
            // so the map-time verification can name the responsible assembly in its diagnostics.
            if (contribution.RequiredJsonRootTypes.Count > 0)
            {
                contributedJsonContracts.Add(new ContributedJsonContract(
                    owner, contribution.JsonContext, contribution.RequiredJsonRootTypes));
            }
        }

        _contributedBindings = bindings.ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
        _contributedHandlers = handlers.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _contributedResultMappers = resultMappers.ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
        _contributedEndpoints = endpoints.ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
        _endpointDescriptors = endpointDescriptors;
        _capabilityDescriptors = capabilityDescriptors;
        _bodyTypes = bodyTypes;
        _requiredIdentities = requiredIdentities;
        _requiredIdentityEntries = requiredIdentityEntries;
        _requiredServiceEntries = requiredServiceEntries;
        _contributedJsonContracts = contributedJsonContracts;
    }

    /// <summary>
    /// Endpoint descriptors this host contributes to the single authoritative registry build.
    /// </summary>
    public IReadOnlyList<CapabilityEndpointDescriptor> GetEndpointDescriptors() => _endpointDescriptors;

    /// <summary>
    /// Capability descriptors contributed by this host. Capability registry aggregation is
    /// performed by the host bootstrap chain, not by this activation.
    /// </summary>
    public IReadOnlyList<CapabilityDescriptor> GetCapabilityDescriptors() => _capabilityDescriptors;

    /// <summary>
    /// Required registry identities contributed by this host, with owner/action/type diagnostics.
    /// </summary>
    public IReadOnlyList<CapabilityEndpointRequiredIdentity> GetRequiredIdentities() => _requiredIdentities;

    /// <summary>
    /// Required identities together with the assembly that declared each requirement.
    /// </summary>
    internal IReadOnlyList<RequiredIdentityEntry> GetRequiredIdentityEntries() => _requiredIdentityEntries;

    internal IReadOnlyList<RequiredServiceEntry> GetRequiredServiceEntries() => _requiredServiceEntries;

    internal readonly record struct RequiredIdentityEntry(
        CapabilityEndpointRequiredIdentity Identity,
        string RequiredBy);

    /// <summary>One business contract service type this host must have registered (CCHTTP007).</summary>
    internal readonly record struct RequiredServiceEntry(
        string Owner,
        Type ServiceType);

    /// <summary>
    /// Per-contribution JSON contract requirements this host must verify against its own final
    /// <c>Http.Json.JsonOptions</c>. Contributions that declare no required root are not listed: they
    /// have nothing to verify.
    /// </summary>
    internal IReadOnlyList<ContributedJsonContract> GetContributedJsonContracts() => _contributedJsonContracts;

    /// <summary>
    /// One contribution's JSON contract: the declaring owner, the source-generated context it delivers
    /// and the roots the host's final options must answer from that context.
    /// </summary>
    internal readonly record struct ContributedJsonContract(
        string Owner,
        JsonSerializerContext? JsonContext,
        IReadOnlyList<CapabilityEndpointJsonRootType> RequiredJsonRootTypes);

    /// <summary>
    /// Body types contributed by this host, with the legacy static registry as fallback.
    /// </summary>
    public IReadOnlyList<Type> GetBodyTypes()
    {
        var merged = new List<Type>(_bodyTypes);
        foreach (var legacyType in CapabilityEndpointJsonContractRegistry.GetRegisteredBodyTypes())
        {
            if (!merged.Contains(legacyType))
            {
                merged.Add(legacyType);
            }
        }

        return merged;
    }

    /// <summary>
    /// True when this host's contributions declare the (endpointId, version) identity.
    /// Owned endpoints must satisfy the explicit contribution contract; they never depend on
    /// legacy static registration visibility.
    /// </summary>
    public bool OwnsEndpoint(string endpointId, int version)
        => _contributedEndpoints.ContainsKey((endpointId, version));

    public bool TryGetContributedEndpoint(
        string endpointId,
        int version,
        out CapabilityEndpointDescriptor? descriptor)
    {
        if (_contributedEndpoints.TryGetValue((endpointId, version), out var entry))
        {
            descriptor = entry.Descriptor;
            return true;
        }

        descriptor = null;
        return false;
    }

    /// <summary>
    /// Owning assembly name for a contributed endpoint identity, or null when not contributed.
    /// </summary>
    public string? GetEndpointOwner(string endpointId, int version)
        => _contributedEndpoints.TryGetValue((endpointId, version), out var entry) ? entry.Owner : null;

    public bool TryGetContributedBinding(
        string endpointId,
        int version,
        out CapabilityEndpointBindingContract? binding)
    {
        if (_contributedBindings.TryGetValue((endpointId, version), out var entry))
        {
            binding = entry.Binding;
            return true;
        }

        binding = null;
        return false;
    }

    public bool TryGetContributedHandler(string capabilityId, out ICapabilityHandlerInvoker? invoker)
    {
        if (_contributedHandlers.TryGetValue(capabilityId, out var entry))
        {
            invoker = entry.Invoker;
            return true;
        }

        invoker = null;
        return false;
    }

    public bool TryGetContributedResultMapper(
        string endpointId,
        int version,
        out Func<EndpointExecutionContext, IServiceProvider, object>? mapper)
    {
        if (_contributedResultMappers.TryGetValue((endpointId, version), out var entry))
        {
            mapper = entry.Mapper;
            return true;
        }

        mapper = null;
        return false;
    }

    /// <summary>
    /// Host-authoritative binding lookup; the legacy static binding registry remains the fallback
    /// source for existing compatibility consumers.
    /// </summary>
    public CapabilityEndpointBindingContract? TryGetBinding(string endpointId, int version)
        => TryGetContributedBinding(endpointId, version, out var binding)
            ? binding
            : CapabilityEndpointBindingRegistry.Find(endpointId, version);

    /// <summary>
    /// Host-authoritative handler lookup; the legacy static resolver remains the fallback source
    /// for existing compatibility consumers.
    /// </summary>
    public ICapabilityHandlerInvoker? TryGetHandler(string capabilityId)
        => _contributedHandlers.TryGetValue(capabilityId, out var entry)
            ? entry.Invoker
            : CapabilityHandlerResolverProvider.GetConcreteResolver().Resolve(capabilityId);

    /// <summary>
    /// Host-authoritative result mapper lookup; the host's result contract registry (fed by legacy
    /// pending registrations) remains the fallback source for existing compatibility consumers.
    /// </summary>
    public Func<EndpointExecutionContext, IServiceProvider, object>? TryGetResultMapper(
        string endpointId,
        int version)
        => TryGetContributedResultMapper(endpointId, version, out var mapper)
            ? mapper
            : _legacyResultContracts.TryGetResultMapper(endpointId, version);
}
