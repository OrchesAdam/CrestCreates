using System.Text.Json.Serialization;

namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class AssetOperationResult
{
    [JsonRequired]
    public Guid AssetId { get; set; }
    [JsonRequired]
    public string Status { get; set; } = string.Empty;
    public string? WorkflowInstanceId { get; set; }
    public string? HumanTaskId { get; set; }
}
