using System.ComponentModel;
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
    /// Registry identities this contribution requires to be present in the authoritative registry
    /// after build (or verified against an already-built registry). Missing identities fail the
    /// map deterministically with owner/action/type diagnostics; they are never resolved through
    /// a fallback and never silently skipped.
    /// </summary>
    public IReadOnlyList<CapabilityEndpointRequiredIdentity> RequiredIdentityIds { get; init; }
        = Array.Empty<CapabilityEndpointRequiredIdentity>();
}
