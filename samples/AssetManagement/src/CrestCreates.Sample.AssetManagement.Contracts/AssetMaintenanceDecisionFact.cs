namespace CrestCreates.Sample.AssetManagement.Contracts;

public sealed record AssetMaintenanceDecisionFact
{
    public Guid AssetId { get; init; }
    public string ApproverId { get; init; } = string.Empty;
    public bool Approved { get; init; }
    public string Note { get; init; } = string.Empty;
}
