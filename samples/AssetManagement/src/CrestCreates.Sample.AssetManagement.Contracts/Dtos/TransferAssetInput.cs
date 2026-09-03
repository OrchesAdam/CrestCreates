namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class TransferAssetInput
{
    public Guid AssetId { get; set; }
    public Guid OrganizationId { get; set; }
    public string? Location { get; set; }
}
