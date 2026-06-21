namespace CrestCreates.Agent.DraftContracts.Dto;

public sealed record AgentDraftContractError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
}
