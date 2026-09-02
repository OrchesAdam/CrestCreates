namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class MaintenanceRequestInput
{
    public Guid AssetId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
