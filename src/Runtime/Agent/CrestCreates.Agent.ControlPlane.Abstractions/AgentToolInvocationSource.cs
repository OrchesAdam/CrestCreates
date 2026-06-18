namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum AgentToolInvocationSource
{
    Direct,
    McpAdapter,
    HttpAdapter,
    CliAdapter,
    Internal
}
