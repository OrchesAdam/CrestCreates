using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Result of confirming that a governance audit finalization actually persisted
/// in the auditor. <see cref="Completed"/> is the only terminal acknowledgement;
/// anything else keeps the invocation fenced from further progress.
/// </summary>
internal enum AgentToolAuditConfirmation
{
    Unconfirmed,
    Completed,
    Indeterminate,
    Conflict
}

/// <summary>
/// Shared pre-dispatch / post-dispatch finalizer helpers. This is the single
/// place that settles budgets, fences indeterminate attempts, releases audited
/// attempts before dispatch, records governance decisions, and confirms audit
/// finalizations. Both the live pre-dispatch coordinator and the invoker's
/// dispatch fence / post-dispatch paths consume these helpers so every
/// participant converges a terminal state through the same code.
/// </summary>
internal sealed class AgentToolPreDispatchFinalizer
{
    private readonly IAgentToolInvocationGate _invocations;
    private readonly IAgentToolBudgetGate _budget;
    private readonly IAgentToolGovernanceAuditor _audit;
    private readonly IAgentToolInvocationLeaseAbandoner _leaseAbandoner;

    public AgentToolPreDispatchFinalizer(
        IAgentToolInvocationGate invocations,
        IAgentToolBudgetGate budget,
        IAgentToolGovernanceAuditor audit,
        IAgentToolInvocationLeaseAbandoner leaseAbandoner)
    {
        _invocations = invocations ?? throw new ArgumentNullException(nameof(invocations));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _leaseAbandoner = leaseAbandoner ?? throw new ArgumentNullException(nameof(leaseAbandoner));
    }

    internal async ValueTask<AgentToolAuditConfirmation> QueryAuditConfirmationAsync(
        string auditId,
        AgentToolGovernanceFinalizationRecord expected)
    {
        try
        {
            var result = await _audit.GetFinalizationStateAsync(
                auditId,
                expected.Context.LogicalInvocationKey.TenantId,
                CancellationToken.None)
                .ConfigureAwait(false);
            return ResolveAuditConfirmation(result, expected);
        }
        catch
        {
            return AgentToolAuditConfirmation.Unconfirmed;
        }
    }

    internal async ValueTask<AgentToolAuditConfirmation> ConfirmAuditFinalizationAsync(
        AgentToolGovernanceFinalizationRecord expected,
        AgentToolAuditMode auditMode)
    {
        if (auditMode is not (AgentToolAuditMode.Required or AgentToolAuditMode.BestEffort))
            return AgentToolAuditConfirmation.Unconfirmed;

        AgentToolAuditConfirmation direct;
        try
        {
            var result = await _audit.FinalizeAsync(expected, CancellationToken.None)
                .ConfigureAwait(false);
            direct = ResolveAuditConfirmation(result, expected);
        }
        catch
        {
            direct = AgentToolAuditConfirmation.Unconfirmed;
        }

        if (direct == AgentToolAuditConfirmation.Completed)
            return direct;

        // A non-equivalent direct response may be stale. The durable AuditId
        // query is authoritative before fencing or accepting the terminal state.
        var queried = await QueryAuditConfirmationAsync(expected.AuditId, expected)
            .ConfigureAwait(false);
        return queried == AgentToolAuditConfirmation.Unconfirmed
            && direct != AgentToolAuditConfirmation.Unconfirmed
            ? AgentToolAuditConfirmation.Conflict
            : queried;
    }

    internal static AgentToolAuditConfirmation ResolveAuditConfirmation(
        AgentToolGovernanceFinalizationResult result,
        AgentToolGovernanceFinalizationRecord expected)
    {
        if (result.Status != AgentToolGovernanceFinalizationStatus.Finalized
            || result.Record is null)
            return AgentToolAuditConfirmation.Unconfirmed;
        if (EquivalentFinalization(result.Record, expected))
            return AgentToolAuditConfirmation.Completed;
        if (SameFinalizationIdentity(result.Record, expected)
            && result.Record.AttemptState == AgentToolGovernanceAttemptFinalState.Indeterminate
            && result.Record.InvocationState is null or AgentToolInvocationTerminalState.Indeterminate)
            return AgentToolAuditConfirmation.Indeterminate;
        return AgentToolAuditConfirmation.Conflict;
    }

    internal static bool MatchesPreparedCompletion(
        AgentToolInvocationCompletionResult result,
        AgentToolInvocationOutcome expectedOutcome,
        string? expectedAuditId,
        string expectedReservationId,
        string expectedReasonCode)
        => result.State == AgentToolInvocationCompletionState.Completed
            && EquivalentOutcome(result.Outcome, expectedOutcome)
            && result.PreparedAt.HasValue
            && string.Equals(result.AuditId, expectedAuditId, StringComparison.Ordinal)
            && string.Equals(
                result.BudgetReservationId,
                expectedReservationId,
                StringComparison.Ordinal)
            && string.Equals(result.ReasonCode, expectedReasonCode, StringComparison.Ordinal);

    internal static bool EquivalentFinalization(
        AgentToolGovernanceFinalizationRecord left,
        AgentToolGovernanceFinalizationRecord right)
        => SameFinalizationIdentity(left, right)
            && EquivalentContext(left.Context, right.Context)
            && left.DispatchStarted == right.DispatchStarted
            && left.BudgetReservation.Equals(right.BudgetReservation)
            && left.AttemptState == right.AttemptState
            && left.InvocationState == right.InvocationState
            && string.Equals(
                left.OutcomeHash ?? AgentToolGovernanceOutcomeHasher.Compute(left.Outcome, left.AuditFacts),
                right.OutcomeHash ?? AgentToolGovernanceOutcomeHasher.Compute(right.Outcome, right.AuditFacts),
                StringComparison.Ordinal)
            && string.Equals(left.ReasonCode, right.ReasonCode, StringComparison.Ordinal);

    internal static bool EquivalentContext(
        AgentToolGovernanceAuditContext left,
        AgentToolGovernanceAuditContext right)
        => left.LogicalInvocationKey == right.LogicalInvocationKey
            && string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal)
            && string.Equals(left.InvocationFingerprint, right.InvocationFingerprint, StringComparison.Ordinal)
            && string.Equals(left.ArgumentsHash, right.ArgumentsHash, StringComparison.Ordinal)
            && left.ArgumentsEvaluated == right.ArgumentsEvaluated
            && left.CallOrigin == right.CallOrigin
            && string.Equals(left.AgentRolesHash, right.AgentRolesHash, StringComparison.Ordinal)
            && left.ToolContract.Equals(right.ToolContract)
            && left.CapabilityContract.Equals(right.CapabilityContract)
            && Equals(left.InputSchemaContract, right.InputSchemaContract)
            && Equals(left.OutputSchemaContract, right.OutputSchemaContract)
            && left.Governance.Equals(right.Governance);

    internal static bool SameFinalizationIdentity(
        AgentToolGovernanceFinalizationRecord left,
        AgentToolGovernanceFinalizationRecord right)
        => string.Equals(left.AuditId, right.AuditId, StringComparison.Ordinal)
            && left.Context.LogicalInvocationKey == right.Context.LogicalInvocationKey
            && string.Equals(left.Context.AttemptId, right.Context.AttemptId, StringComparison.Ordinal)
            && string.Equals(
                left.Context.InvocationFingerprint,
                right.Context.InvocationFingerprint,
                StringComparison.Ordinal)
            && string.Equals(left.Lease.LeaseId, right.Lease.LeaseId, StringComparison.Ordinal)
            && string.Equals(left.Lease.AttemptId, right.Lease.AttemptId, StringComparison.Ordinal)
            && left.Lease.FencingToken == right.Lease.FencingToken
            && string.Equals(
                left.BudgetReservation.ReservationId,
                right.BudgetReservation.ReservationId,
                StringComparison.Ordinal);

    internal static bool EquivalentOutcome(
        AgentToolInvocationOutcome? left,
        AgentToolInvocationOutcome right)
        => left is not null
            && left.Kind == right.Kind
            && string.Equals(left.Code, right.Code, StringComparison.Ordinal)
            && string.Equals(left.Message, right.Message, StringComparison.Ordinal)
            && left.Issues.SequenceEqual(right.Issues)
            && (!left.StructuredOutput.HasValue == !right.StructuredOutput.HasValue
                && (!left.StructuredOutput.HasValue
                    || string.Equals(
                        left.StructuredOutput.Value.GetRawText(),
                        right.StructuredOutput?.GetRawText(),
                        StringComparison.Ordinal)));

    internal async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateWithSettledBudgetAsync(
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation settled,
        string reasonCode)
        => await FinalizeIndeterminateAfterGateAsync(
            auditHandle,
            auditContext,
            lease,
            settled,
            dispatchStarted: true,
            reasonCode).ConfigureAwait(false);

    internal async ValueTask<AgentToolInvocationOutcome> FinalizeIndeterminateAfterGateAsync(
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        bool dispatchStarted,
        string reasonCode,
        IReadOnlyList<AgentToolAuditFact>? auditFacts = null)
    {
        var outcome = Indeterminate(reasonCode);
        var invocationPersisted = await TryMarkIndeterminateAsync(lease, reasonCode)
            .ConfigureAwait(false);
        var auditReason = invocationPersisted ? reasonCode : "invocation_completion_uncertain";
        if (auditHandle is not null)
        {
            _ = await ConfirmAuditFinalizationAsync(
                Finalization(
                    auditHandle,
                    auditContext,
                    lease,
                    reservation,
                    dispatchStarted,
                    AgentToolGovernanceAttemptFinalState.Indeterminate,
                    invocationPersisted ? AgentToolInvocationTerminalState.Indeterminate : null,
                    outcome,
                    auditReason),
                auditContext.Governance.EffectiveAuditMode).ConfigureAwait(false);
        }

        return outcome;
    }

    internal async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateAsync(
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        string reasonCode)
    {
        AgentToolBudgetReservation settled;
        try
        {
            settled = await FinalizeBudgetAsync(reservation, AgentToolBudgetReservationState.Indeterminate, reasonCode, auditContext.LogicalInvocationKey.TenantId)
                .ConfigureAwait(false);
        }
        catch
        {
            return await FinishIndeterminateWithoutBudgetAsync(
                auditHandle, auditContext, lease, reservation, "budget_settlement_failure").ConfigureAwait(false);
        }

        return await FinalizeIndeterminateAfterGateAsync(
            auditHandle,
            auditContext,
            lease,
            settled,
            dispatchStarted: true,
            reasonCode).ConfigureAwait(false);
    }

    internal async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateWithoutBudgetAsync(
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        string reasonCode,
        bool dispatchStarted = true)
    {
        var unknownReservation = reservation with
        {
            State = AgentToolBudgetReservationState.Unknown
        };
        return await FinalizeIndeterminateAfterGateAsync(
            auditHandle,
            auditContext,
            lease,
            unknownReservation,
            dispatchStarted,
            reasonCode).ConfigureAwait(false);
    }

    internal async ValueTask<AgentToolInvocationOutcome> ReleaseAuditedBeforeDispatchAsync(
        AgentToolRuntimeEntry entry,
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        string reasonCode)
    {
        AgentToolBudgetReservation released;
        try
        {
            released = await FinalizeBudgetAsync(reservation, AgentToolBudgetReservationState.Released, reasonCode, auditContext.LogicalInvocationKey.TenantId)
                .ConfigureAwait(false);
        }
        catch
        {
            return await FinishIndeterminateWithoutBudgetAsync(
                auditHandle,
                auditContext,
                lease,
                reservation,
                "budget_settlement_failure",
                dispatchStarted: false).ConfigureAwait(false);
        }

        try
        {
            await _invocations.PrepareReleaseAsync(
                lease,
                new AgentToolInvocationPrepareReleaseRequest
                {
                    AuditId = auditHandle?.AuditId,
                    BudgetReservationId = released.ReservationId,
                    ReasonCode = reasonCode
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return await FinalizeIndeterminateAfterGateAsync(
                auditHandle,
                auditContext,
                lease,
                released,
                dispatchStarted: false,
                reasonCode).ConfigureAwait(false);
        }

        var outcome = Outcome(
            AgentToolInvocationOutcomeKind.InProgress,
            "AGENT_TOOL_INVOCATION_NOT_ACQUIRED",
            "The tool invocation could not acquire execution ownership.");
        if (auditHandle is not null)
        {
            var confirmation = await ConfirmAuditFinalizationAsync(
                Finalization(
                    auditHandle, auditContext, lease, released, false,
                    AgentToolGovernanceAttemptFinalState.Released,
                    null,
                    outcome,
                    reasonCode),
                entry.Governance.EffectiveAuditMode).ConfigureAwait(false);
            if (confirmation is AgentToolAuditConfirmation.Indeterminate
                or AgentToolAuditConfirmation.Conflict)
            {
                if (confirmation == AgentToolAuditConfirmation.Conflict)
                    return Indeterminate("pre_dispatch_audit_conflict");

                var fenced = await TryMarkIndeterminateAsync(
                    lease,
                    "pre_dispatch_audit_indeterminate").ConfigureAwait(false);
                return fenced
                    ? Indeterminate("pre_dispatch_audit_indeterminate")
                    : GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE");
            }
            else if (entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required
                && confirmation != AgentToolAuditConfirmation.Completed)
                outcome = GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE");
        }

        if (entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required
            && outcome.Kind == AgentToolInvocationOutcomeKind.GovernanceDenied)
            return outcome;

        try
        {
            var published = await _invocations.PublishReleaseAsync(lease, CancellationToken.None)
                .ConfigureAwait(false);
            return MatchesPublishedRelease(
                published,
                auditHandle?.AuditId,
                released.ReservationId,
                reasonCode)
                ? outcome
                : await ResolveReleaseUncertaintyAsync(
                    lease,
                    auditHandle?.AuditId,
                    released.ReservationId,
                    reasonCode).ConfigureAwait(false);
        }
        catch
        {
            return await ResolveReleaseUncertaintyAsync(
                lease,
                auditHandle?.AuditId,
                released.ReservationId,
                reasonCode).ConfigureAwait(false);
        }
    }

    internal async ValueTask<AgentToolInvocationOutcome> ResolveReleaseUncertaintyAsync(
        AgentToolInvocationLease lease,
        string? auditId,
        string reservationId,
        string reasonCode)
    {
        try
        {
            var state = await _invocations.GetReleaseStateAsync(lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (state.State == AgentToolInvocationReleaseState.Released
                && state.PreparedAt.HasValue
                && string.Equals(state.AuditId, auditId, StringComparison.Ordinal)
                && string.Equals(state.BudgetReservationId, reservationId, StringComparison.Ordinal)
                && string.Equals(state.ReasonCode, reasonCode, StringComparison.Ordinal))
            {
                return Outcome(
                    AgentToolInvocationOutcomeKind.InProgress,
                    "AGENT_TOOL_INVOCATION_NOT_ACQUIRED",
                    "The tool invocation could not acquire execution ownership.");
            }

            if (state.State == AgentToolInvocationReleaseState.Indeterminate)
            {
                await MarkIndeterminateIgnoringFailureAsync(
                    lease,
                    "pre_dispatch_release_uncertain").ConfigureAwait(false);
                return Indeterminate("pre_dispatch_release_uncertain");
            }
        }
        catch
        {
            // The durable gate remains fenced when release publication cannot be confirmed.
        }

        return Indeterminate("pre_dispatch_release_uncertain");
    }

    internal static bool MatchesPublishedRelease(
        AgentToolInvocationReleaseResult result,
        string? auditId,
        string reservationId,
        string reasonCode)
        => result.State == AgentToolInvocationReleaseState.Released
            && result.PreparedAt.HasValue
            && string.Equals(result.AuditId, auditId, StringComparison.Ordinal)
            && string.Equals(result.BudgetReservationId, reservationId, StringComparison.Ordinal)
            && string.Equals(result.ReasonCode, reasonCode, StringComparison.Ordinal);

    internal async ValueTask<AgentToolInvocationOutcome> FinishFenceIndeterminateAsync(
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation? reservation,
        string reasonCode)
    {
        // When the fencing lookup itself failed, no reservation was produced — there
        // is no budget to settle, so go straight to the gate-side indeterminate fence.
        if (reservation is null)
        {
            return await FinalizeIndeterminateAfterGateAsync(
                auditHandle,
                auditContext,
                lease,
                new AgentToolBudgetReservation
                {
                    ReservationId = string.Empty,
                    AttemptId = lease.AttemptId,
                    InvocationFingerprint = auditContext.InvocationFingerprint,
                    Category = "unknown",
                    CostUnits = 0,
                    State = AgentToolBudgetReservationState.Unknown
                },
                dispatchStarted: false,
                reasonCode).ConfigureAwait(false);
        }

        AgentToolBudgetReservation settled;
        try
        {
            // This worker has not called Dispatcher, so business budget may be
            // released even though the durable fencing transition is unknown.
            settled = await FinalizeBudgetAsync(
                    reservation,
                    AgentToolBudgetReservationState.Released,
                    reasonCode,
                    auditContext.LogicalInvocationKey.TenantId)
                .ConfigureAwait(false);
        }
        catch
        {
            return await FinishIndeterminateWithoutBudgetAsync(
                auditHandle,
                auditContext,
                lease,
                reservation,
                "budget_settlement_failure",
                dispatchStarted: false).ConfigureAwait(false);
        }

        return await FinalizeIndeterminateAfterGateAsync(
            auditHandle,
            auditContext,
            lease,
            settled,
            dispatchStarted: false,
            reasonCode).ConfigureAwait(false);
    }

    internal ValueTask<AgentToolBudgetReservation> FinalizeBudgetAsync(
        AgentToolBudgetReservation reservation,
        AgentToolBudgetReservationState state,
        string reasonCode,
        string? tenantId)
        => _budget.FinalizeAsync(
            new AgentToolBudgetFinalizeRequest
            {
                ReservationId = reservation.ReservationId,
                AttemptId = reservation.AttemptId,
                InvocationFingerprint = reservation.InvocationFingerprint,
                RequestedState = state,
                ReasonCode = reasonCode,
                TenantId = tenantId
            },
            CancellationToken.None);

    internal static AgentToolGovernanceFinalizationRecord Finalization(
        AgentToolGovernancePreDispatchReceipt handle,
        AgentToolGovernanceAuditContext context,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        bool dispatchStarted,
        AgentToolGovernanceAttemptFinalState attemptState,
        AgentToolInvocationTerminalState? invocationState,
        AgentToolInvocationOutcome outcome,
        string reasonCode,
        IReadOnlyList<AgentToolAuditFact>? auditFacts = null)
        => new()
        {
            AuditId = handle.AuditId,
            Context = context,
            Lease = lease,
            DispatchStarted = dispatchStarted,
            BudgetReservation = reservation,
            AttemptState = attemptState,
            InvocationState = invocationState,
            Outcome = outcome,
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(outcome, auditFacts ?? Array.Empty<AgentToolAuditFact>()),
            AuditFacts = auditFacts ?? Array.Empty<AgentToolAuditFact>(),
            ReasonCode = reasonCode
        };

    internal async ValueTask AbandonUnrecordedLeaseBestEffortAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        _ = await TryAbandonUnrecordedLeaseAsync(lease, reasonCode).ConfigureAwait(false);
    }

    internal async ValueTask<bool> TryAbandonUnrecordedLeaseAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        try
        {
            await _leaseAbandoner.AbandonUnrecordedLeaseAsync(
                lease,
                reasonCode,
                CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // A stale or expired lease is already fenced from dispatch.
            return false;
        }
    }

    internal async ValueTask<AgentToolInvocationOutcome> MarkIndeterminateBestEffortAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        await MarkIndeterminateIgnoringFailureAsync(lease, reasonCode).ConfigureAwait(false);
        return Indeterminate(reasonCode);
    }

    internal async ValueTask MarkIndeterminateIgnoringFailureAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        _ = await TryMarkIndeterminateAsync(lease, reasonCode).ConfigureAwait(false);
    }

    internal async ValueTask<bool> TryMarkIndeterminateAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        try
        {
            await _invocations.MarkIndeterminateAsync(lease, reasonCode, CancellationToken.None)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            // The durable gate or its reconciler remains authoritative when ownership was lost.
            return false;
        }
    }

    internal async ValueTask<bool> RecordDecisionBestEffortAsync(
        AgentToolGovernanceAuditContext context,
        AgentToolGovernanceDecisionState decision,
        AgentToolInvocationOutcome outcome,
        string reasonCode,
        AgentToolBudgetReservation? observedReservation = null)
    {
        try
        {
            await _audit.RecordDecisionAsync(
                new AgentToolGovernanceDecisionRecord
                {
                    Context = context,
                    Decision = decision,
                    Outcome = outcome,
                    ReasonCode = reasonCode,
                    ObservedReservation = observedReservation
                },
                CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AgentToolInvocationOutcome Outcome(
        AgentToolInvocationOutcomeKind kind,
        string code,
        string message)
        => AgentToolInvocationOutcomeFactory.Outcome(kind, code, message);

    private static AgentToolInvocationOutcome GovernanceDenied(string code)
        => AgentToolInvocationOutcomeFactory.GovernanceDenied(code);

    private static AgentToolInvocationOutcome Indeterminate(string reasonCode)
        => AgentToolInvocationOutcomeFactory.Indeterminate(reasonCode);
}
