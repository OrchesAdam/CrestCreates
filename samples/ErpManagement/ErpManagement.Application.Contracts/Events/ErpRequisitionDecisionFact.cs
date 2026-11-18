namespace ErpManagement.Application.Contracts.Events;

/// <summary>Approval decision fact captured into the HumanTask completion result.</summary>
public sealed class ErpRequisitionDecisionFact
{
    public string RequisitionId { get; init; } = string.Empty;
    public string ApproverId { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
}