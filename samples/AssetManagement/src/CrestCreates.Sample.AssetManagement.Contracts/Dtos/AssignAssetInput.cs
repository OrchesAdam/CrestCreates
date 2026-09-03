namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class AssignAssetInput
{
    public Guid AssetId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
}
