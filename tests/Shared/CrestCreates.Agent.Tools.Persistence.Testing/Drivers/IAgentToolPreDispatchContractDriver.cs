using CrestCreates.Agent.Tools;

namespace CrestCreates.Agent.Tools.Persistence.Testing.Drivers;

public interface IAgentToolPreDispatchContractDriver
{
    IAgentToolGovernanceAuditor Auditor { get; }
    IAgentToolBudgetGate BudgetGate { get; }
    IAgentToolInvocationGate InvocationGate { get; }
}

public interface IDurableAgentToolPreDispatchContractDriver : IAgentToolPreDispatchContractDriver
{
    IAgentToolPreDispatchReconciliationStore ReconciliationStore { get; }

    ValueTask RestartProviderAsync(CancellationToken cancellationToken = default);
}
