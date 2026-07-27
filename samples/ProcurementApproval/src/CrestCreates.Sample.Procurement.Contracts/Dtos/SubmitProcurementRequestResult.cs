namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class SubmitProcurementRequestResult
{
    public required Guid RequestId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public bool RequiresApproval { get; init; }
}
