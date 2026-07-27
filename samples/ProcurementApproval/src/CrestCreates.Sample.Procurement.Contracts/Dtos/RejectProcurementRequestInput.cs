namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class RejectProcurementRequestInput
{
    public required Guid RequestId { get; init; }
    public required string ApproverId { get; init; }
    public required string Reason { get; init; }
}
