namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class UpdateAssetInput
{
    public Guid AssetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Location { get; set; }
}
