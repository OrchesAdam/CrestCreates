namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IAgentToolInvocationAuditor
{
    Task RecordAsync(AgentToolInvocationAuditRecord record, CancellationToken ct = default);
}
