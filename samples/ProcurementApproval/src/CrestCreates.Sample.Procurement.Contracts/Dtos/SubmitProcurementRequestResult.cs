namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class SubmitProcurementRequestResult
{
    public Guid RequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
}
