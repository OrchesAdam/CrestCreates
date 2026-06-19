namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum AgentToolResultStatus
{
    Success,
    SucceededWithDiagnostics,
    Denied,
    Failed,
    NotFound,
    InvalidRequest
}
