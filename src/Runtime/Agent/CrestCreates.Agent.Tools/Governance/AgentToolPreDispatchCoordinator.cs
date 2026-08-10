using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Result of the live pre-dispatch coordinator. Either the attempt is
/// <see cref="Authorized"/> to proceed to Dispatch (carrying the bound budget
/// reservation and the accepted governance receipt), or it reached a terminal
/// outcome and must not proceed.
/// </summary>
internal enum AgentToolPreDispatchAuthorizationKind
{
    Authorized,
    Terminal
}

/// <summary>
/// Immutable input for the live pre-dispatch coordinator. Carries the runtime
/// entry (for effective audit mode and release-time finalization), the acquired
/// lease, the governance context, its audit projection, and the safe approval
/// result.
/// </summary>
internal sealed record AgentToolPreDispatchCoordinationRequest
{
    public required AgentToolRuntimeEntry Entry { get; init; }

    public required AgentToolInvocationLease Lease { get; init; }

    public required AgentToolGovernanceContext Governance { get; init; }

    public required AgentToolGovernanceAuditContext AuditContext { get; init; }

    public required AgentToolApprovalResult Approval { get; init; }
}

/// <summary>
/// The coordinator's decision. On <see cref="AgentToolPreDispatchAuthorizationKind.Authorized"/>
/// the caller may proceed to Dispatch with the returned reservation and receipt;
/// on <see cref="AgentToolPreDispatchAuthorizationKind.Terminal"/> the outcome is
/// final and Dispatch must not be attempted.
/// </summary>
internal sealed record AgentToolPreDispatchAuthorization
{
    public required AgentToolPreDispatchAuthorizationKind Kind { get; init; }

    public AgentToolBudgetReservation? Reservation { get; init; }

    public AgentToolGovernancePreDispatchReceipt? Receipt { get; init; }

    public AgentToolInvocationOutcome? Outcome { get; init; }
}

/// <summary>
/// Live pre-dispatch coordinator. Owns the single protocol sequence from
/// Prepare Intent through Reserve Budget, Bind Reservation, Record Checkpoint
/// and Bind Accepted, including authoritative recovery when a write response is
/// lost. The invoker only proceeds to Dispatch after this component authorizes
/// the attempt; the dispatch fence itself remains in the invoker.
/// </summary>
internal sealed class AgentToolPreDispatchCoordinator
{
    private readonly IAgentToolInvocationGate _invocations;
    private readonly IAgentToolBudgetGate _budget;
    private readonly IAgentToolGovernanceAuditor _audit;
    private readonly AgentToolPreDispatchFinalizer _finalizer;

    public AgentToolPreDispatchCoordinator(
        IAgentToolInvocationGate invocations,
        IAgentToolBudgetGate budget,
        IAgentToolGovernanceAuditor audit,
        AgentToolPreDispatchFinalizer finalizer)
    {
        _invocations = invocations ?? throw new ArgumentNullException(nameof(invocations));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer));
    }

    public async ValueTask<AgentToolPreDispatchAuthorization> ExecuteAsync(
        AgentToolPreDispatchCoordinationRequest request,
        CancellationToken cancellationToken = default)
    {
        var entry = request.Entry;
        var lease = request.Lease;
        var governance = request.Governance;
        var auditContext = request.AuditContext;
        var approval = request.Approval;
        var identity = new AgentToolPreDispatchIdentity(
            auditContext.LogicalInvocationKey,
            lease.AttemptId);

        AgentToolInvocationPreDispatchResult? preDispatchState;

        try
        {
            preDispatchState = await _invocations.PreparePreDispatchIntentAsync(
                lease,
                new AgentToolInvocationPreparePreDispatchIntentRequest
                {
                    Intent = new AgentToolInvocationPreDispatchIntentSnapshot
                    {
                        FrozenLease = lease,
                        InvocationFingerprint = auditContext.InvocationFingerprint,
                        Context = auditContext,
                        Approval = approval
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Use CancellationToken.None for recovery — the caller token is cancelled.
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
            {
                await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, "pre_dispatch_intent_uncertain")
                    .ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            // Use CancellationToken.None for recovery — the caller token may be in a bad state.
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
                return await TerminalAsync(
                    _finalizer.FinishFenceIndeterminateAsync(
                        null, auditContext, lease, null, "pre_dispatch_intent_uncertain"))
                    .ConfigureAwait(false);
        }

        // If the attempt was already abandoned (e.g. budget denial replay), return the abandoned receipt.
        if (preDispatchState.State == AgentToolInvocationPreDispatchState.Abandoned
            && preDispatchState.AbandonedReceipt is not null)
        {
            return Terminal(GovernanceDenied(preDispatchState.AbandonedReceipt.ReasonCode));
        }

        // Validate Prepare returned the expected Pending state.
        if (preDispatchState.State != AgentToolInvocationPreDispatchState.Pending)
        {
            return await TerminalAsync(
                _finalizer.FinishFenceIndeterminateAsync(
                    null, auditContext, lease, null,
                    $"pre_dispatch_intent_unexpected_state:{preDispatchState.State}"))
                .ConfigureAwait(false);
        }

        AgentToolBudgetReserveResult reserved;
        try
        {
            reserved = await _budget.ReserveAsync(
                new AgentToolBudgetReserveRequest { Context = governance },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Authoritative recovery: check if the reservation was actually persisted.
            var reservationRead = await RecoverReservationStateAsync(identity, CancellationToken.None)
                .ConfigureAwait(false);
            if (reservationRead is { Status: AgentToolBudgetReadStatus.Reserved } read
                && read.Reservation is not null
                && Matches(read.Reservation, governance))
            {
                reserved = new AgentToolBudgetReserveResult
                {
                    Status = AgentToolBudgetReserveStatus.Reserved,
                    Reservation = read.Reservation
                };
            }
            else
            {
                var decisionOutcome = Indeterminate("AGENT_TOOL_BUDGET_RESERVATION_UNCERTAIN");
                var recorded = await _finalizer.RecordDecisionBestEffortAsync(
                    auditContext,
                    AgentToolGovernanceDecisionState.Indeterminate,
                    decisionOutcome,
                    "budget_reservation_uncertain").ConfigureAwait(false);
                if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                    decisionOutcome = Indeterminate("decision_audit_failure");
                await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, "budget_reservation_uncertain").ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            // Authoritative recovery: check if the reservation was actually persisted.
            var reservationRead = await RecoverReservationStateAsync(identity, CancellationToken.None)
                .ConfigureAwait(false);
            if (reservationRead is { Status: AgentToolBudgetReadStatus.Reserved } read
                && read.Reservation is not null
                && Matches(read.Reservation, governance))
            {
                reserved = new AgentToolBudgetReserveResult
                {
                    Status = AgentToolBudgetReserveStatus.Reserved,
                    Reservation = read.Reservation
                };
            }
            else
            {
                var decisionOutcome = Indeterminate("AGENT_TOOL_BUDGET_FAILURE");
                var recorded = await _finalizer.RecordDecisionBestEffortAsync(
                    auditContext,
                    AgentToolGovernanceDecisionState.Indeterminate,
                    decisionOutcome,
                    "budget_reservation_uncertain").ConfigureAwait(false);
                if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                    decisionOutcome = Indeterminate("decision_audit_failure");
                await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, "budget_reservation_uncertain").ConfigureAwait(false);
                return Terminal(decisionOutcome);
            }
        }

        var isKnownBudgetDenial = reserved.Status == AgentToolBudgetReserveStatus.Denied
            && reserved.Reservation is null
            && !string.IsNullOrWhiteSpace(reserved.ReasonCode);
        if (isKnownBudgetDenial)
        {
            var reason = "budget_denied";
            var decisionOutcome = GovernanceDenied("AGENT_TOOL_BUDGET_DENIED");
            var recorded = await _finalizer.RecordDecisionBestEffortAsync(
                auditContext,
                AgentToolGovernanceDecisionState.Denied,
                decisionOutcome,
                reason).ConfigureAwait(false);
            // Publish the stable Abandoned receipt. If this fails, do NOT clear the
            // Attempt fence — keep it so a future reconciliation can produce the receipt.
            var denialPublished = await PublishBudgetDenialBestEffortAsync(lease, reserved, reason).ConfigureAwait(false);
            if (!denialPublished)
            {
                // Denial receipt could not be persisted — return Indeterminate to keep the fence.
                await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, "budget_denial_receipt_uncertain").ConfigureAwait(false);
                return Terminal(Indeterminate("AGENT_TOOL_BUDGET_DENIALLY_UNCERTAIN"));
            }
            await _finalizer.AbandonUnrecordedLeaseBestEffortAsync(lease, "budget_denied").ConfigureAwait(false);
            return Terminal(!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required
                ? GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE")
                : decisionOutcome);
        }

        if (reserved.Status != AgentToolBudgetReserveStatus.Reserved
            || reserved.Reservation is not { State: AgentToolBudgetReservationState.Reserved } reservation
            || !Matches(reservation, governance))
        {
            const string reason = "budget_reservation_invalid";
            var decisionOutcome = Indeterminate("AGENT_TOOL_BUDGET_FAILURE");
            var recorded = await _finalizer.RecordDecisionBestEffortAsync(
                auditContext,
                AgentToolGovernanceDecisionState.Indeterminate,
                decisionOutcome,
                reason,
                reserved.Reservation).ConfigureAwait(false);
            if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                decisionOutcome = Indeterminate("decision_audit_failure");
            await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, reason).ConfigureAwait(false);
            return Terminal(decisionOutcome);
        }

        try
        {
            preDispatchState = await _invocations.BindPreDispatchReservationAsync(
                lease,
                new AgentToolInvocationBindReservationRequest
                {
                    ReservationId = reservation.ReservationId,
                    Reservation = reservation
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
            {
                await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, "bind_reservation_uncertain")
                    .ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
                return await TerminalAsync(
                    _finalizer.FinishFenceIndeterminateAsync(
                        null, auditContext, lease, reservation, "bind_reservation_uncertain"))
                    .ConfigureAwait(false);
        }

        // Validate Bind returned the expected Ready state.
        if (preDispatchState.State != AgentToolInvocationPreDispatchState.Ready)
        {
            return await TerminalAsync(
                _finalizer.FinishFenceIndeterminateAsync(
                    null, auditContext, lease, reservation,
                    $"bind_reservation_unexpected_state:{preDispatchState.State}"))
                .ConfigureAwait(false);
        }

        var preDispatch = new AgentToolGovernancePreDispatchRecord
        {
            Context = auditContext,
            Lease = lease,
            Approval = approval,
            BudgetReservation = reservation
        };
        AgentToolGovernancePreDispatchReceipt? auditHandle = null;
        try
        {
            var writeResult = await _audit.RecordPreDispatchAsync(preDispatch, cancellationToken)
                .ConfigureAwait(false);
            if (writeResult.Status is AgentToolGovernancePreDispatchWriteStatus.Accepted
                or AgentToolGovernancePreDispatchWriteStatus.Duplicate)
                auditHandle = writeResult.Receipt;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            auditHandle = await RecoverAuditReceiptAsync(preDispatch, CancellationToken.None)
                .ConfigureAwait(false);
            if (auditHandle is not null)
            {
                _ = await _finalizer.ReleaseAuditedBeforeDispatchAsync(
                    entry,
                    auditHandle,
                    auditContext,
                    lease,
                    reservation,
                    "pre_dispatch_cancelled").ConfigureAwait(false);
            }
            else
            {
                _ = await _finalizer.FinishFenceIndeterminateAsync(
                    null,
                    auditContext,
                    lease,
                    reservation,
                    "pre_dispatch_audit_uncertain").ConfigureAwait(false);
            }
            throw;
        }
        catch
        {
            auditHandle = await RecoverAuditReceiptAsync(preDispatch, CancellationToken.None)
                .ConfigureAwait(false);
            if (auditHandle is null)
                return await TerminalAsync(
                    _finalizer.FinishFenceIndeterminateAsync(
                        null, auditContext, lease, reservation, "pre_dispatch_audit_uncertain"))
                    .ConfigureAwait(false);
        }

        try
        {
            preDispatchState = await _invocations.BindAcceptedPreDispatchAsync(
                lease,
                new AgentToolInvocationBindPreDispatchRequest
                {
                    Receipt = auditHandle!
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
            {
                await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, "bind_accepted_uncertain")
                    .ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
                return await TerminalAsync(
                    _finalizer.FinishFenceIndeterminateAsync(
                        auditHandle, auditContext, lease, reservation, "bind_accepted_uncertain"))
                    .ConfigureAwait(false);
        }

        // Validate BindAccepted returned the expected Accepted state with the exact receipt.
        if (preDispatchState.State != AgentToolInvocationPreDispatchState.Accepted
            || preDispatchState.AcceptedReceipt is null
            || !string.Equals(preDispatchState.AcceptedReceipt.AuditId, auditHandle!.AuditId, StringComparison.Ordinal)
            || !string.Equals(preDispatchState.AcceptedReceipt.Identity.AttemptId, auditHandle.Identity.AttemptId, StringComparison.Ordinal)
            || !preDispatchState.AcceptedReceipt.Identity.LogicalInvocationKey.Equals(auditHandle.Identity.LogicalInvocationKey)
            || preDispatchState.AcceptedReceipt.AcceptedAt != auditHandle.AcceptedAt)
        {
            return await TerminalAsync(
                _finalizer.FinishFenceIndeterminateAsync(
                    auditHandle, auditContext, lease, reservation,
                    $"bind_accepted_unexpected_state:{preDispatchState.State}"))
                .ConfigureAwait(false);
        }

        return new AgentToolPreDispatchAuthorization
        {
            Kind = AgentToolPreDispatchAuthorizationKind.Authorized,
            Reservation = reservation,
            Receipt = auditHandle
        };
    }

    internal async ValueTask<AgentToolBudgetReservationReadResult?> RecoverReservationStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _budget.GetReservationStateAsync(identity, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    internal async ValueTask<AgentToolGovernancePreDispatchReceipt?> RecoverAuditReceiptAsync(
        AgentToolGovernancePreDispatchRecord record,
        CancellationToken cancellationToken)
    {
        // Authoritative recovery: query the auditor for the persisted state first.
        var identity = new AgentToolPreDispatchIdentity(record.Context.LogicalInvocationKey, record.Context.AttemptId);
        AgentToolGovernancePreDispatchReadResult readResult;
        try
        {
            readResult = await _audit.GetPreDispatchStateAsync(identity, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Provider unavailable / timeout — the lookup did NOT complete. We
            // cannot distinguish "write never landed" from "write landed and
            // response was lost", so a retry write would create a second fuzzy
            // commit window. Keep the worker fenced and go Indeterminate.
            return null;
        }

        if (readResult.Status == AgentToolGovernancePreDispatchReadStatus.Accepted
            && readResult.Receipt is not null
            && readResult.Checkpoint is not null)
        {
            // Validate the full checkpoint against the expected record using the shared comparer.
            if (!AgentToolGovernancePreDispatchComparer.Equivalent(readResult.Checkpoint, record))
                return null; // Checkpoint mismatch — cannot safely proceed.
            // Validate receipt identity matches.
            if (!string.Equals(readResult.Receipt.Identity.AttemptId, record.Context.AttemptId, StringComparison.Ordinal))
                return null;
            return readResult.Receipt;
        }

        if (readResult.Status != AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            // Invalid/unknown lookup outcome — do not rewrite.
            return null;
        }

        // Authoritative Missing: this live worker may perform one bounded retry
        // of the identical record. A second retry is not allowed.
        try
        {
            var writeResult = await _audit.RecordPreDispatchAsync(record, cancellationToken)
                .ConfigureAwait(false);
            return writeResult.Status is AgentToolGovernancePreDispatchWriteStatus.Accepted
                or AgentToolGovernancePreDispatchWriteStatus.Duplicate
                ? writeResult.Receipt
                : null;
        }
        catch
        {
            return null;
        }
    }

    internal async ValueTask<AgentToolInvocationPreDispatchResult?> RecoverPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _invocations.GetPreDispatchStateAsync(identity, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    internal async ValueTask<bool> PublishBudgetDenialBestEffortAsync(
        AgentToolInvocationLease lease,
        AgentToolBudgetReserveResult reserved,
        string reasonCode)
    {
        try
        {
            await _invocations.PublishBudgetDenialAsync(
                lease,
                new AgentToolInvocationPublishDenialRequest
                {
                    Outcome = GovernanceDenied("AGENT_TOOL_BUDGET_DENIED"),
                    ReasonCode = reasonCode
                },
                CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool Matches(
        AgentToolBudgetReservation reservation,
        AgentToolGovernanceContext context)
        => string.Equals(reservation.AttemptId, context.AttemptId, StringComparison.Ordinal)
            && string.Equals(reservation.InvocationFingerprint, context.InvocationFingerprint, StringComparison.Ordinal)
            && string.Equals(reservation.Category, context.Governance.Budget.Category, StringComparison.Ordinal)
            && reservation.CostUnits == context.Governance.Budget.CostUnits
            && reservation.MaxCallsPerExecution == context.Governance.Budget.MaxCallsPerExecution;

    private async ValueTask<AgentToolPreDispatchAuthorization> TerminalAsync(
        ValueTask<AgentToolInvocationOutcome> outcomeTask)
        => Terminal(await outcomeTask.ConfigureAwait(false));

    private static AgentToolPreDispatchAuthorization Terminal(AgentToolInvocationOutcome outcome)
        => new()
        {
            Kind = AgentToolPreDispatchAuthorizationKind.Terminal,
            Outcome = outcome
        };

    private static AgentToolInvocationOutcome GovernanceDenied(string code)
        => AgentToolInvocationOutcomeFactory.GovernanceDenied(code);

    private static AgentToolInvocationOutcome Indeterminate(string reasonCode)
        => AgentToolInvocationOutcomeFactory.Indeterminate(reasonCode);
}
