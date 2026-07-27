namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class ApproveProcurementRequestInput
{
    public required Guid RequestId { get; init; }
    public required string ApproverId { get; init; }
    public required string Comment { get; init; }
}
