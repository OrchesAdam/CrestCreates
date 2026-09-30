using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record EvidencePreviewEntry(
    string DraftId,
    string TenantId,
    PackageEvidencePreview Preview);
