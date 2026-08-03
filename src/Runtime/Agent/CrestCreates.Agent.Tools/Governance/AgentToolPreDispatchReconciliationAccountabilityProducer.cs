namespace CrestCreates.Agent.Tools.Governance;

internal sealed class AgentToolPreDispatchReconciliationAccountabilityProducer : IAgentToolPreDispatchReconciliationAccountabilityProducer
{
    public ValueTask PublishAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
