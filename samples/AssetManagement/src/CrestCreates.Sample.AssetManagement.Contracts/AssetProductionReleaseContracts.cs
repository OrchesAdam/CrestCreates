using CrestCreates.Sample.AssetManagement.Contracts.Mes;

namespace CrestCreates.Sample.AssetManagement.Contracts;

public sealed record AssetProductionLockFact
{
    public Guid AssetId { get; init; }
    public string WorkflowInstanceId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; init; }
}

public sealed record AssetMaintenanceEvidenceFact
{
    public Guid AssetId { get; init; }
    public string RequesterId { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public bool Approved { get; init; }
    public string Note { get; init; } = string.Empty;
    public TrialObservation? Observation { get; init; }
}

public sealed record AssetProductionReleaseInput
{
    public Guid AssetId { get; set; }
    public Guid ObservationId { get; set; }
    public Guid SourceEventId { get; set; }
    public string WorkflowInstanceId { get; set; } = string.Empty;
}

public sealed record AssetProductionReleaseFact
{
    public Guid OperationId { get; init; }
    public ProductionReleaseReceipt Receipt { get; init; } = default!;
}
