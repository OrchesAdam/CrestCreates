using System.ComponentModel;

namespace CrestCreates.DynamicApi;

/// <summary>
/// One authoritative registry identity required by a contribution, together with the
/// deterministic diagnostics context (owner, action, type) reported when the identity is missing.
/// </summary>
/// <param name="IdentityId">Endpoint descriptor id that must exist in the host's authoritative registry.</param>
/// <param name="Owner">Owner identity for diagnostics (assembly/service). Falls back to the contribution owner when empty.</param>
/// <param name="Action">Action identity for diagnostics (for example <c>Create</c>).</param>
/// <param name="TypeName">Domain/DTO type identity for diagnostics.</param>
/// <param name="Version">Required descriptor version; <c>0</c> means any version of the identity.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CapabilityEndpointRequiredIdentity(
    string IdentityId,
    string Owner,
    string Action,
    string TypeName,
    int Version = 0);
