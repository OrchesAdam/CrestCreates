namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class RejectProcurementRequestInput
{
    public Guid RequestId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
