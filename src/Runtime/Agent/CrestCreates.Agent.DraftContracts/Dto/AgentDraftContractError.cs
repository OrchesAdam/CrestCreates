using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.DraftContracts.Dto;

public sealed record AgentDraftContractError
{
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
}
