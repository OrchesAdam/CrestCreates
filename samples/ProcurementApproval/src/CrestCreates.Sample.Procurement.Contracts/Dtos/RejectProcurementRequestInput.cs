namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class RejectProcurementRequestInput
{
    public Guid RequestId { get; set; }
    public string ApproverId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
