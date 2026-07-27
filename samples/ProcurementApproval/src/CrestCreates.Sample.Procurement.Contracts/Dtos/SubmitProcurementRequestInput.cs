namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class SubmitProcurementRequestInput
{
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string RequesterId { get; init; }
    public required string Category { get; init; }
}
