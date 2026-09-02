namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class AssetQueryInput
{
    public Guid? AssetId { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public Guid? OrganizationId { get; set; }
}
