namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ActivationReadinessPreview
{
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required bool IsReady { get; init; }
    public required IReadOnlyList<ActivationReadinessBlocker> Blockers { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public string? ReviewResultId { get; init; }
    public string? PackagePreviewId { get; init; }
}
