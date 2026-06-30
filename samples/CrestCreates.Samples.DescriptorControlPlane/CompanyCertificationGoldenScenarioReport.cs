namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed record CompanyCertificationGoldenScenarioReport
{
    public required string ScenarioName { get; init; }

    public required bool ControlPlanePassed { get; init; }

    public required string GovernanceDecision { get; init; }

    public string WorkflowStatus { get; init; } = "NotExecuted";

    public string HumanTaskStatus { get; init; } = "NotExecuted";

    public bool ApprovedEventCaptured { get; init; }

    public bool SubmittedEventCaptured { get; init; }

    public bool RuntimeExecuted { get; init; }

    public bool RuntimeBlockedByGovernance { get; init; }

    public string? WorkflowInstanceId { get; init; }

    public string? HumanTaskInstanceId { get; init; }

    // ── Runtime Proof ──
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
    public bool RuntimeProofUsedFreshActivatedHost { get; init; }

    public string? ErrorMessage { get; init; }
}
