namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IAgentToolAuthorizationService
{
    Task<AgentToolAuthorizationResult> AuthorizeAsync(
        AgentToolInvocationContext context,
        AgentToolPermissionRequirement permission,
        string expectedToolName,
        CancellationToken ct = default);
}
