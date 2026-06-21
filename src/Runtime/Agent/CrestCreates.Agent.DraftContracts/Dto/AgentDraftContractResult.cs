namespace CrestCreates.Agent.DraftContracts.Dto;

public sealed record AgentDraftContractResult<T> where T : class
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public IReadOnlyList<AgentDraftContractError> Errors { get; init; } = [];

    public static AgentDraftContractResult<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static AgentDraftContractResult<T> Failure(IReadOnlyList<AgentDraftContractError> errors) => new() { IsSuccess = false, Errors = errors };
    public static AgentDraftContractResult<T> Failure(AgentDraftContractError error) => Failure([error]);
}
