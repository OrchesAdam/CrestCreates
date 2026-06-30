using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public sealed record CompanyCertificationAuthoringGoldenScenarioReport
{
    // ── Authoring ──
    public required bool AuthoringSucceeded { get; init; }
    public string? AuthoringError { get; init; }

    // ── Draft Set Review ──
    public required bool DraftSetBlocked { get; init; }
    public string? BlockReason { get; init; }
    public required string FinalDecisionSource { get; init; }
    public required IReadOnlyList<IDescriptor> FinalProposedInventory { get; init; }

    // ── Activation ──
    public string? ActivationRequestId { get; init; }
    public string? ActivationSubjectDraftId { get; init; }
    public string? BoundPackageEvidenceHash { get; init; }
    public string? BoundPackageEvidenceEnvelopeHash { get; init; }
    public bool RuntimeActivationGateSucceeded { get; init; }

    // ── Runtime Proof ──
    public bool RuntimeProofUsedFreshActivatedHost { get; init; }
    public string? ActivatedWorkflowDescriptorId { get; init; }
    public int? ActivatedWorkflowVersion { get; init; }
    public IReadOnlyList<string> ActivatedHumanTaskDescriptorIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ObservedHumanTaskDescriptorIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WorkflowStepSequence { get; init; } = Array.Empty<string>();
    public string? InitialReviewHumanTaskInstanceId { get; init; }
    public string? FinanceReviewHumanTaskInstanceId { get; init; }
    public int CompletedHumanTaskCount { get; init; }
    public string? ActivatedInventoryHash { get; init; }
    public string? ActivatedPackageEvidenceHash { get; init; }
    public bool ApprovedEventCaptured { get; init; }
    public bool RuntimeExecuted { get; init; }
    public string? ErrorMessage { get; init; }
}
