namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class ApproveProcurementRequestInput
{
    public Guid RequestId { get; set; }
    public string Comment { get; set; } = string.Empty;
}
