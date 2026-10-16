using System.ComponentModel;

namespace CrestCreates.DynamicApi;

/// <summary>
/// One JSON contract root a contribution requires on its host, together with the diagnostics context
/// (action and wire component) reported when the host's final <c>Http.Json.JsonOptions</c> cannot
/// answer it from the declared source-generated contract.
/// </summary>
/// <remarks>
/// #128 §7.2: the CRUD metadata must be provided by the declared source-generated contributor. The
/// map-time verification requires both the contribution's own <c>JsonContext</c> and the host's final
/// options to answer every entry from that context, so a host that only carries a reflection resolver
/// — or a resolver chain that answers the type before the generated context — fails deterministically
/// instead of silently serializing through reflection metadata. The owner identity always comes from
/// the declaring contribution; entries only carry the per-root diagnostics context, mirroring
/// <see cref="CapabilityEndpointRequiredIdentity"/>.
/// </remarks>
/// <param name="ClrType">The CLR type whose source-generated metadata this contribution requires.</param>
/// <param name="Action">Action identity for diagnostics (for example <c>Create</c> or <c>Search</c>).</param>
/// <param name="Component">
/// Wire component of the root, using the §7.1 vocabulary: <c>Body</c>, <c>CapabilityInput</c>,
/// <c>Payload</c>, <c>ResultEnvelope</c> or <c>VoidEnvelope</c>.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CapabilityEndpointJsonRootType(Type ClrType, string Action, string Component)
{
    /// <summary>Readable type identity used by the deterministic failure diagnostics.</summary>
    public string TypeName => ClrType.FullName ?? ClrType.Name;
}
