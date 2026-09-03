namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class RegisterAssetInput
{
    public string AssetTag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public string? Location { get; set; }
}
