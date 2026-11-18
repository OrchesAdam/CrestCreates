namespace ErpManagement.Application.Contracts.DTOs;

/// <summary>Approval-center projection of one pending requisition approval HumanTask.</summary>
public class ApprovalTaskDto
{
    public string HumanTaskId { get; set; } = string.Empty;
    public Guid RequisitionId { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string RequesterId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ApprovalDecisionInput
{
    public string Comment { get; set; } = string.Empty;
}