namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IAgentToolAuthorizationService
{
    Task<AgentToolAuthorizationResult> AuthorizeAsync(
        AgentToolInvocationContext context,
        AgentToolPermissionRequirement permission,
        CancellationToken ct = default);
}
