using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Assertions;
using CrestCreates.Agent.Tools.Persistence.Testing.Drivers;

namespace CrestCreates.Agent.Tools.Persistence.Testing.Cases;

/// <summary>
/// Shared semantic contract cases for the Invocation Gate pre-dispatch fence
/// (Pending → Ready → Accepted → DispatchStarted). Activated by concrete
/// runners in Slice 3+.
/// </summary>
public static class AgentToolPreDispatchFenceContractCases
{
    public static async Task H03_AcceptedResponseLossShouldBeReconciledByAttemptIdentity(
        IAgentToolPreDispatchContractDriver driver,
        AgentToolInvocationLease lease,
        AgentToolLogicalInvocationKey logicalInvocationKey,
        AgentToolInvocationPreparePreDispatchIntentRequest intentRequest,
        AgentToolInvocationBindReservationRequest bindReservationRequest,
        AgentToolInvocationBindPreDispatchRequest bindPreDispatchRequest,
        CancellationToken cancellationToken)
    {
        await driver.InvocationGate.PreparePreDispatchIntentAsync(
            lease, intentRequest, cancellationToken);

        await driver.InvocationGate.BindPreDispatchReservationAsync(
            lease, bindReservationRequest, cancellationToken);

        var bindResult = await driver.InvocationGate.BindAcceptedPreDispatchAsync(
            lease, bindPreDispatchRequest, cancellationToken);

        var identity = new AgentToolPreDispatchIdentity(
            logicalInvocationKey,
            lease.AttemptId);

        var stateRead = await driver.InvocationGate.GetPreDispatchStateAsync(
            identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.True(
            stateRead.State == AgentToolInvocationPreDispatchState.Accepted,
            $"State should be Accepted after response loss recovery, got {stateRead.State}.");
    }
}
