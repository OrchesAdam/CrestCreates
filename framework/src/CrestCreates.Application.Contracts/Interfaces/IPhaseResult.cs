namespace CrestCreates.Application.Contracts.Interfaces;

/// <summary>
/// Non-generic marker interface so ExecutePhaseAsync can accept any phase result type.
/// All phase result types implement this pattern (bool Success, string? Error).
/// </summary>
public interface IPhaseResult
{
    bool Success { get; }
    string? Error { get; }
}
