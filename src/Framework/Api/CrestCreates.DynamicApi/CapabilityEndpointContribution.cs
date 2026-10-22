using System.ComponentModel;
using System.Text.Json.Serialization;
using CrestCreates.Metadata;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Immutable description of everything one generated assembly contributes to
/// CapabilityEndpoint activation on a host.
/// </summary>
/// <remarks>
/// Contributions are registered explicitly per host through
/// <see cref="CapabilityEndpointExtensions.AddCrestCapabilityEndpointContribution"/> and are
/// aggregated by <see cref="CapabilityEndpointHostActivation"/>. Generated code emits one
/// concrete contribution type per assembly (a derived type), so
/// <c>TryAddEnumerable</c> can de-duplicate repeated activation by implementation type.
/// Contributions never capture <see cref="IServiceProvider"/>, scopes, or mutable host state,
/// and they never write to process-global static registries.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public class CapabilityEndpointContribution
{
    /// <summary>
    /// Assembly that owns this contribution. Used as the per-host diagnostic owner identity and
    /// validated when the host activation is constructed.
    /// </summary>
    public string OwnerAssemblyName { get; init; } = string.Empty;

    /// <summary>
    /// Capability descriptors contributed by this assembly. Capability registry aggregation is
    /// performed by the host bootstrap chain, not by this contribution.
    /// </summary>
    public IReadOnlyList<CapabilityDescriptor> CapabilityDescriptors { get; init; }
        = Array.Empty<CapabilityDescriptor>();

    /// <summary>
    /// Capability endpoint descriptors to merge into this host's authoritative endpoint registry build.
    /// </summary>
    public IReadOnlyList<CapabilityEndpointDescriptor> EndpointDescriptors { get; init; }
        = Array.Empty<CapabilityEndpointDescriptor>();

    /// <summary>
    /// Input binding contracts keyed by (endpoint id, version).
    /// </summary>
    public IReadOnlyList<CapabilityEndpointBindingContract> Bindings { get; init; }
        = Array.Empty<CapabilityEndpointBindingContract>();

    /// <summary>
    /// Handler invokers keyed by capability id.
    /// </summary>
    public IReadOnlyList<CapabilityEndpointHandlerBinding> Handlers { get; init; }
        = Array.Empty<CapabilityEndpointHandlerBinding>();

    /// <summary>
    /// Result mappers keyed by (endpoint id, version).
    /// </summary>
    public IReadOnlyList<CapabilityEndpointResultBinding> ResultMappers { get; init; }
        = Array.Empty<CapabilityEndpointResultBinding>();

    /// <summary>
    /// Body types that require source-generated JSON metadata on this host.
    /// </summary>
    public IReadOnlyList<Type> BodyTypes { get; init; }
        = Array.Empty<Type>();

    /// <summary>
    /// The assembly's source-generated JSON context (the generated CRUD root context) this
    /// contribution delivers to the host's final <c>Http.Json.JsonOptions</c>.
    /// </summary>
    /// <remarks>
    /// #128 §7.1/§7.2: the generated configurator appended by the assembly activation wires this
    /// context into the host's resolver chain, and the map-time verification requires every
    /// <see cref="RequiredJsonRootTypes"/> entry to be answered by this exact context on the host's
    /// final options. A reflection resolver never satisfies that requirement.
    /// </remarks>
    public JsonSerializerContext? JsonContext { get; init; }

    /// <summary>
    /// JSON contract roots this contribution requires the host's final options to resolve from
    /// <see cref="JsonContext"/> (bodies, capability input envelopes, payloads, typed result envelopes
    /// and the void envelope). Each root that the final options cannot answer from the declared
    /// source-generated context fails the map deterministically with owner/action/type diagnostics.
    /// </summary>
    public IReadOnlyList<CapabilityEndpointJsonRootType> RequiredJsonRootTypes { get; init; }
        = Array.Empty<CapabilityEndpointJsonRootType>();

    /// <summary>
    /// Registry identities this contribution requires to be present in the authoritative registry
    /// after build (or verified against an already-built registry). Missing identities fail the
    /// map deterministically with owner/action/type diagnostics; they are never resolved through
    /// a fallback and never silently skipped.
    /// </summary>
    public IReadOnlyList<CapabilityEndpointRequiredIdentity> RequiredIdentityIds { get; init; }
        = Array.Empty<CapabilityEndpointRequiredIdentity>();

    /// <summary>
    /// Business contract service types this contribution's generated invokers resolve from DI.
    /// The host activation verifies every type is registered before any endpoint is mapped
    /// (CCHTTP007); a missing registration is a startup failure, never a request-time 500.
    /// </summary>
    public IReadOnlyList<Type> RequiredServiceTypes { get; init; }
        = Array.Empty<Type>();
}
