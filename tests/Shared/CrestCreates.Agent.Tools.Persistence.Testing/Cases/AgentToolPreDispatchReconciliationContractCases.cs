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

    /// <summary>
    /// The runtime may produce a StillPending observation with a null ReasonCode (no
    /// reason family). Durable providers must persist it and read it back as null —
    /// e.g. via an empty-string sentinel when the column is NOT NULL.
    /// </summary>
    public static async Task NullReasonObservation_Should_RoundTripAsNull(
        IDurableAgentToolPreDispatchContractDriver driver,
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        var observation = new AgentToolPreDispatchReconciliationObservation
        {
            Identity = identity,
            Status = AgentToolPreDispatchReconciliationStatus.StillPending,
            ReasonCode = null,
            ObservedAt = DateTimeOffset.UtcNow,
            Revision = 1
        };

        var inserted = await driver.ReconciliationStore.TryUpsertObservationAsync(
            observation, 0, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(inserted, "null-reason observation insert must succeed.");

        var read = await driver.ReconciliationStore.ReadObservationAsync(identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(read is not null, "null-reason observation must be readable.");
        AgentToolPreDispatchContractAssertions.True(
            read!.ReasonCode is null,
            "a persisted null ReasonCode must round-trip as null, not become a sentinel.");
    }

    /// <summary>
    /// The runtime may produce a terminal Conflict receipt with a null ReasonCode.
    /// Durable providers must persist it and read it back as null.
    /// </summary>
    public static async Task NullReasonTerminalReceipt_Should_RoundTripAsNull(
        IDurableAgentToolPreDispatchContractDriver driver,
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        var receipt = new AgentToolPreDispatchReconciliationReceipt
        {
            Identity = identity,
            Status = AgentToolPreDispatchReconciliationStatus.Conflict,
            ReasonCode = null,
            TerminalAt = TruncateToMicroseconds(DateTimeOffset.UtcNow),
            IntegrityValue = "integrity-null-reason"
        };

        var inserted = await driver.ReconciliationStore.TryInsertReceiptAsync(receipt, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(inserted, "null-reason terminal receipt insert must succeed.");

        var read = await driver.ReconciliationStore.ReadReceiptAsync(identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(read is not null, "null-reason terminal receipt must be readable.");
        AgentToolPreDispatchContractAssertions.True(
            read!.ReasonCode is null,
            "a persisted null ReasonCode must round-trip as null, not become a sentinel.");
    }

    /// <summary>
    /// A null-reason terminal receipt must survive a provider restart and replay as
    /// null after it — the durable form is what CAS losers converge on.
    /// </summary>
    public static async Task NullReasonTerminalReceipt_Should_ReplayAfterRestart(
        IDurableAgentToolPreDispatchContractDriver driver,
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        var receipt = new AgentToolPreDispatchReconciliationReceipt
        {
            Identity = identity,
            Status = AgentToolPreDispatchReconciliationStatus.Conflict,
            ReasonCode = null,
            TerminalAt = TruncateToMicroseconds(DateTimeOffset.UtcNow),
            IntegrityValue = "integrity-null-reason-restart"
        };

        var inserted = await driver.ReconciliationStore.TryInsertReceiptAsync(receipt, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(inserted, "null-reason terminal receipt insert must succeed.");

        await driver.RestartProviderAsync(cancellationToken);

        var read = await driver.ReconciliationStore.ReadReceiptAsync(identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(read is not null, "null-reason terminal receipt must survive restart.");
        AgentToolPreDispatchContractAssertions.True(
            read!.ReasonCode is null,
            "a persisted null ReasonCode must remain null after restart, not become a sentinel.");
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var micros = value.Ticks / 10L * 10L;
        return new DateTimeOffset(new DateTime(micros, value.DateTime.Kind), value.Offset);
    }
}
