using System.Text.Json;

namespace CrestCreates.Agent.Tools;

public sealed record AgentToolInvocationRequest(
    string ToolName,
    JsonElement? Arguments = null,
    string? ApprovalEvidence = null);

public enum AgentToolInvocationOutcomeKind
{
    Unknown = 0,
    Succeeded = 1,
    UnknownTool = 2,
    InvalidRequest = 3,
    GovernanceDenied = 4,
    InProgress = 5,
    InvocationConflict = 6,
    InvocationIndeterminate = 7,
    CapabilityFailure = 8,
    InternalContractFailure = 9,
    InternalServer = 10
}

public sealed record AgentToolInvocationIssue(
    string Code,
    string? FieldPath = null);

public sealed record AgentToolInvocationOutcome
{
    public required AgentToolInvocationOutcomeKind Kind { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public JsonElement? StructuredOutput { get; init; }

    public IReadOnlyList<AgentToolInvocationIssue> Issues { get; init; }
        = Array.Empty<AgentToolInvocationIssue>();

    public bool IsSuccess => Kind == AgentToolInvocationOutcomeKind.Succeeded;
}

public interface IAgentToolInvoker
{
    ValueTask<AgentToolInvocationOutcome> InvokeAsync(
        AgentToolInvocationRequest request,
        CancellationToken cancellationToken = default);
}
