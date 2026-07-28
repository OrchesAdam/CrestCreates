using System.Text.Json.Serialization;

namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class ProcurementRequestResult
{
    [JsonRequired]
    public Guid Id { get; set; }
    [JsonRequired]
    public Guid RequestId { get; set; }
    [JsonRequired]
    public string Title { get; set; } = string.Empty;
    [JsonRequired]
    public string Description { get; set; } = string.Empty;
    [JsonRequired]
    public decimal Amount { get; set; }
    [JsonRequired]
    public string Currency { get; set; } = string.Empty;
    [JsonRequired]
    public string RequesterId { get; set; } = string.Empty;
    [JsonRequired]
    public string Category { get; set; } = string.Empty;
    [JsonRequired]
    public string Status { get; set; } = string.Empty;
    public string? ApproverId { get; set; }
    public string? WorkflowInstanceId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
}
