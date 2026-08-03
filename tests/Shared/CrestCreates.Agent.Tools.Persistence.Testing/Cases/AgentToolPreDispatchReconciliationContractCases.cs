using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Assertions;
using CrestCreates.Agent.Tools.Persistence.Testing.Drivers;

namespace CrestCreates.Agent.Tools.Persistence.Testing.Cases;

/// <summary>
/// Shared semantic contract cases for the default pre-dispatch reconciler.
/// Activated by concrete runners in Slice 4+.
/// </summary>
public static class AgentToolPreDispatchReconciliationContractCases
{
    public static async Task H06_RestartedReconcilerShouldNotAutoDispatch(
        IDurableAgentToolPreDispatchContractDriver driver,
        IAgentToolPreDispatchReconciler reconciler,
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        await driver.RestartProviderAsync(cancellationToken);

        var result = await reconciler.ReconcileAsync(identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.True(
            result.Status != AgentToolPreDispatchReconciliationStatus.StillPending
            && result.Status != AgentToolPreDispatchReconciliationStatus.Unknown,
            "Reconciler must never auto-dispatch.");
    }
}
