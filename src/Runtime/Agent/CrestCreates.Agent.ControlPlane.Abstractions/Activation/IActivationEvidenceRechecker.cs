namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Rechecks binding snapshot hashes against current descriptor/draft/review state.
/// If any hash differs from the bound value, the activation request becomes stale.
/// 
/// This is the evidence integrity gate — it prevents activation when the world has
/// changed since the request was created (draft modified, review superseded, etc.).
/// </summary>
public interface IActivationEvidenceRechecker
{
    /// <summary>
    /// Rechecks all hashes in the binding snapshot against current state.
    /// Returns a result indicating whether evidence is still valid, and which
    /// specific hashes (if any) have drifted.
    /// </summary>
    Task<ActivationEvidenceRecheckResult> RecheckAsync(
        string tenantId,
        ActivationBindingSnapshot bindingSnapshot,
        CancellationToken ct = default);
}

/// <summary>
/// Result of an evidence recheck operation.
/// </summary>
public sealed record ActivationEvidenceRecheckResult
{
    public required bool IsStale { get; init; }
    public required IReadOnlyList<ActivationEvidenceDrift> Drifts { get; init; }

    /// <summary>
    /// Quick check: no drift detected.
    /// </summary>
    public bool IsValid => !IsStale;
}

/// <summary>
/// A single hash drift between bound and current state.
/// </summary>
public sealed record ActivationEvidenceDrift
{
    public required string FieldName { get; init; }
    public required string BoundHashValue { get; init; }
    public required string CurrentHashValue { get; init; }
}
