namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class MaintenanceDecisionInput
{
    public Guid AssetId { get; set; }
    public bool Approved { get; set; }
    public string Note { get; set; } = string.Empty;
}
