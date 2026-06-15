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

    public string? ErrorMessage { get; init; }
}
