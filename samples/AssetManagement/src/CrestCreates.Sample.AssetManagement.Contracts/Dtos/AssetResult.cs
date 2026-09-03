using System.Text.Json.Serialization;

namespace CrestCreates.Sample.AssetManagement.Contracts.Dtos;

public sealed class AssetResult
{
    [JsonRequired]
    public Guid Id { get; set; }
    [JsonRequired]
    public string TenantId { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    [JsonRequired]
    public string AssetTag { get; set; } = string.Empty;
    [JsonRequired]
    public string Name { get; set; } = string.Empty;
    [JsonRequired]
    public string Description { get; set; } = string.Empty;
    [JsonRequired]
    public string Category { get; set; } = string.Empty;
    public string? Location { get; set; }
    [JsonRequired]
    public string Status { get; set; } = string.Empty;
    public string? AssignedUserId { get; set; }
    public Guid? ActiveAssignmentId { get; set; }
    public string? MaintenanceWorkflowInstanceId { get; set; }
    [JsonRequired]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}
