namespace ErpManagement.Application.Contracts.DTOs;

public sealed class SalesApprovalTaskDto
{
    public string HumanTaskId { get; init; } = string.Empty;
    public Guid SalesOrderId { get; init; }
    public string SalesOrderNumber { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? RequesterId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
