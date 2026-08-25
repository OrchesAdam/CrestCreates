namespace CrestCreates.Sample.Procurement.Contracts;

/// <summary>Closed, durable decision identity admitted before HumanTask completion.</summary>
public sealed record ProcurementHumanTaskDecisionFact
{
    public required Guid RequestId { get; init; }
    public required string ApproverId { get; init; }
    public required string Comment { get; init; }
}
