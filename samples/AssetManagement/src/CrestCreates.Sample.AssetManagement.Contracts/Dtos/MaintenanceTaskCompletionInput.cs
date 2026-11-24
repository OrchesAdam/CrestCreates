namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class MaintenanceTaskCompletionInput
{
    public Guid AssetId { get; set; }
    public string HumanTaskId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}
