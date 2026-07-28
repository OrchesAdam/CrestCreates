using System.Text.Json.Serialization;

namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class SubmitProcurementRequestResult
{
    [JsonRequired]
    public Guid RequestId { get; set; }
    [JsonRequired]
    public string Status { get; set; } = string.Empty;
    [JsonRequired]
    public decimal Amount { get; set; }
    [JsonRequired]
    public string Currency { get; set; } = string.Empty;
    [JsonRequired]
    public bool RequiresApproval { get; set; }
}
