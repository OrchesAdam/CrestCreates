namespace CrestCreates.Sample.Procurement.Contracts.Dtos;

public sealed class SubmitProcurementRequestInput
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string RequesterId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
