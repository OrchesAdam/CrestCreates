namespace CrestCreates.Agent.Prompting.Abstractions;

public sealed record AgentPromptDiagnostic
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Severity { get; init; } = "Information";
}
