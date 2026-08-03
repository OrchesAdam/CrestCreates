using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolInvoker : IAgentToolInvoker
{
    private readonly AgentToolRuntimeSnapshotProvider _snapshots;
    private readonly IAgentExecutionContextAccessor _execution;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IAgentToolInvocationGate _invocations;
    private readonly IAgentToolInvocationLeaseAbandoner _leaseAbandoner;
    private readonly IAgentToolApprovalGate _approval;
    private readonly IAgentToolBudgetGate _budget;
    private readonly IAgentToolGovernanceAuditor _audit;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly ISchemaValidator _schemas;
    private readonly AgentToolInvocationFingerprintBuilder _fingerprints;
    private readonly AgentCapabilityIdempotencyKeyBuilder _idempotency;
    private readonly AgentToolResultMapper _results;
    private readonly ISchemaRegistry? _schemaRegistry;

    public AgentToolInvoker(
        AgentToolRuntimeSnapshotProvider snapshots,
        IAgentExecutionContextAccessor execution,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IAgentToolInvocationGate invocations,
        IAgentToolInvocationLeaseAbandoner leaseAbandoner,
        IAgentToolApprovalGate approval,
        IAgentToolBudgetGate budget,
        IAgentToolGovernanceAuditor audit,
        ICapabilityDispatcher dispatcher,
        ISchemaValidator schemas,
        AgentToolInvocationFingerprintBuilder fingerprints,
        AgentCapabilityIdempotencyKeyBuilder idempotency,
        AgentToolResultMapper results,
        ISchemaRegistry? schemaRegistry = null)
    {
        _snapshots = snapshots;
        _execution = execution;
        _currentUser = currentUser;
        _tenant = tenant;
        _invocations = invocations;
        _leaseAbandoner = leaseAbandoner;
        _approval = approval;
        _budget = budget;
        _audit = audit;
        _dispatcher = dispatcher;
        _schemas = schemas;
        _fingerprints = fingerprints;
        _idempotency = idempotency;
        _results = results;
        _schemaRegistry = schemaRegistry;
    }

    public async ValueTask<AgentToolInvocationOutcome> InvokeAsync(
        AgentToolInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ToolName))
            return Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_INVALID_REQUEST", "The tool request is invalid.");

        var execution = _execution.Current;
        if (!AgentToolCatalog.IsValid(execution) || !TryGetTrustedIdentity(out var tenantId, out var userId))
            return Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_INVALID_CONTEXT", "A valid trusted execution context is required.");

        var entry = _snapshots.GetRequired().Find(request.ToolName);
        if (entry is null)
            return Outcome(AgentToolInvocationOutcomeKind.UnknownTool, "AGENT_TOOL_UNKNOWN", "The requested tool is unavailable.");

        var key = new AgentToolLogicalInvocationKey(
            tenantId,
            userId,
            execution!.AgentId,
            execution.ExecutionId,
            execution.InvocationId);
        var decisionAttemptId = $"decision-{Guid.NewGuid():N}";

        var roleDeniedBeforeArguments = !entry.AllowedAgentRoles.Overlaps(execution.AgentRoles);
        var selectionDeniedBeforeArguments = execution.CallOrigin == AgentToolCallOrigin.AutomaticSelection
            && entry.Governance.SelectionPolicy != AgentToolSelectionPolicy.AutomaticAllowed;
        if (roleDeniedBeforeArguments || selectionDeniedBeforeArguments)
        {
            var context = CreateGovernanceContext(
                entry, execution, key, decisionAttemptId, NotEvaluatedFingerprint(), argumentsEvaluated: false);
            return await RecordDecisionAndReturnAsync(
                context,
                AgentToolGovernanceDecisionState.Denied,
                Outcome(AgentToolInvocationOutcomeKind.UnknownTool, "AGENT_TOOL_UNKNOWN", "The requested tool is unavailable."),
                roleDeniedBeforeArguments ? "role_denied" : "selection_policy_denied").ConfigureAwait(false);
        }

        if (!TryNormalizeArguments(request.Arguments, out var arguments, out var argumentFailure))
        {
            var context = CreateGovernanceContext(
                entry, execution, key, decisionAttemptId, RawArgumentsFingerprint(request.Arguments));
            return await RecordDecisionAndReturnAsync(
                context,
                AgentToolGovernanceDecisionState.Denied,
                argumentFailure!,
                "invalid_arguments").ConfigureAwait(false);
        }
        if (HasDuplicateProperties(arguments))
        {
            var context = CreateGovernanceContext(
                entry,
                execution,
                key,
                decisionAttemptId,
                TryBuildDecisionFingerprint(entry, execution, key, arguments));
            return await RecordDecisionAndReturnAsync(
                context,
                AgentToolGovernanceDecisionState.Denied,
                Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_DUPLICATE_ARGUMENT", "Tool arguments contain duplicate properties."),
                "duplicate_arguments").ConfigureAwait(false);
        }
        if (entry.InputSchema is not null)
        {
            var validation = _schemas.Validate(entry.InputSchema, arguments, rejectUnknownProperties: true);
            if (!validation.IsValid)
            {
                var context = CreateGovernanceContext(
                    entry,
                    execution,
                    key,
                    decisionAttemptId,
                    TryBuildDecisionFingerprint(entry, execution, key, arguments));
                var invalid = new AgentToolInvocationOutcome
                {
                    Kind = AgentToolInvocationOutcomeKind.InvalidRequest,
                    Code = "AGENT_TOOL_INVALID_ARGUMENTS",
                    Message = "Tool arguments are invalid.",
                    Issues = validation.Errors.Select(error =>
                        new AgentToolInvocationIssue(error.ErrorCode, error.FieldName)).ToArray()
                };
                return await RecordDecisionAndReturnAsync(
                    context,
                    AgentToolGovernanceDecisionState.Denied,
                    invalid,
                    "schema_validation_failed").ConfigureAwait(false);
            }
        }
        else if (arguments.EnumerateObject().Any())
        {
            var context = CreateGovernanceContext(
                entry,
                execution,
                key,
                decisionAttemptId,
                TryBuildDecisionFingerprint(entry, execution, key, arguments));
            return await RecordDecisionAndReturnAsync(
                context,
                AgentToolGovernanceDecisionState.Denied,
                Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_ARGUMENTS_NOT_ACCEPTED", "This tool does not accept arguments."),
                "arguments_not_accepted").ConfigureAwait(false);
        }

        var decisionContext = CreateGovernanceContext(
            entry,
            execution,
            key,
            decisionAttemptId,
            _fingerprints.Build(entry, execution, key, arguments));

        object? input;
        try
        {
            input = await entry.Binding.Contract.BindInputAsync(
                arguments,
                entry.Binding.InputTypeInfo,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return await RecordDecisionAndReturnAsync(
                decisionContext,
                AgentToolGovernanceDecisionState.Denied,
                Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_INVALID_ARGUMENTS", "Tool arguments are invalid."),
                "input_binding_failed").ConfigureAwait(false);
        }

        var fingerprint = _fingerprints.Build(entry, execution, key, arguments);
        var acquired = await _invocations.AcquireAsync(
            new AgentToolInvocationAcquireRequest(key, fingerprint.Value),
            cancellationToken).ConfigureAwait(false);
        if (acquired.Status != AgentToolInvocationAcquireStatus.Acquired)
            return MapAcquire(acquired);
        if (acquired.Lease is null)
            return Outcome(AgentToolInvocationOutcomeKind.InternalServer, "AGENT_TOOL_GATE_INVALID_RESULT", "The tool could not be started.");

        var lease = acquired.Lease;
        await using var renewal = new LeaseRenewal(_invocations, lease);
        var governance = CreateGovernanceContext(entry, execution, key, lease, fingerprint);

        AgentToolApprovalResult approval;
        try
        {
            approval = await _approval.EvaluateAndClaimAsync(
                new AgentToolApprovalRequest { Context = governance, OpaqueEvidence = request.ApprovalEvidence },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AbandonUnrecordedLeaseBestEffortAsync(lease, "approval_cancelled").ConfigureAwait(false);
            throw;
        }
        catch
        {
            var decisionOutcome = Indeterminate("AGENT_TOOL_APPROVAL_FAILURE");
            var recorded = await RecordDecisionBestEffortAsync(
                CreateAuditContext(governance),
                AgentToolGovernanceDecisionState.Indeterminate,
                decisionOutcome,
                "approval_failure").ConfigureAwait(false);
            if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                decisionOutcome = Indeterminate("decision_audit_failure");
            await MarkIndeterminateIgnoringFailureAsync(lease, "approval_failure").ConfigureAwait(false);
            return decisionOutcome;
        }

        if (!IsAcceptedApproval(approval))
        {
            var recorded = await RecordDecisionBestEffortAsync(
                CreateAuditContext(governance),
                AgentToolGovernanceDecisionState.Denied,
                GovernanceDenied("AGENT_TOOL_APPROVAL_DENIED"),
                "approval_denied").ConfigureAwait(false);
            await AbandonUnrecordedLeaseBestEffortAsync(lease, "approval_denied").ConfigureAwait(false);
            return !recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required
                ? GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE")
                : GovernanceDenied("AGENT_TOOL_APPROVAL_DENIED");
        }

        var auditContext = CreateAuditContext(governance);
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
                await MarkIndeterminateIgnoringFailureAsync(lease, "pre_dispatch_intent_uncertain")
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
                return await FinishFenceIndeterminateAsync(
                    null, auditContext, lease, null, "pre_dispatch_intent_uncertain")
                    .ConfigureAwait(false);
        }

        // Validate Prepare returned the expected Pending state.
        if (preDispatchState.State != AgentToolInvocationPreDispatchState.Pending)
        {
            return await FinishFenceIndeterminateAsync(
                null, auditContext, lease, null,
                $"pre_dispatch_intent_unexpected_state:{preDispatchState.State}")
                .ConfigureAwait(false);
        }

        if (preDispatchState.State == AgentToolInvocationPreDispatchState.Abandoned
            && preDispatchState.AbandonedReceipt is not null)
        {
            return GovernanceDenied(preDispatchState.AbandonedReceipt.ReasonCode);
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
                var recorded = await RecordDecisionBestEffortAsync(
                    auditContext,
                    AgentToolGovernanceDecisionState.Indeterminate,
                    decisionOutcome,
                    "budget_reservation_uncertain").ConfigureAwait(false);
                if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                    decisionOutcome = Indeterminate("decision_audit_failure");
                await MarkIndeterminateIgnoringFailureAsync(lease, "budget_reservation_uncertain").ConfigureAwait(false);
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
                var recorded = await RecordDecisionBestEffortAsync(
                    auditContext,
                    AgentToolGovernanceDecisionState.Indeterminate,
                    decisionOutcome,
                    "budget_reservation_uncertain").ConfigureAwait(false);
                if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                    decisionOutcome = Indeterminate("decision_audit_failure");
                await MarkIndeterminateIgnoringFailureAsync(lease, "budget_reservation_uncertain").ConfigureAwait(false);
                return decisionOutcome;
            }
        }

        var isKnownBudgetDenial = reserved.Status == AgentToolBudgetReserveStatus.Denied
            && reserved.Reservation is null
            && !string.IsNullOrWhiteSpace(reserved.ReasonCode);
        if (isKnownBudgetDenial)
        {
            var reason = "budget_denied";
            var decisionOutcome = GovernanceDenied("AGENT_TOOL_BUDGET_DENIED");
            var recorded = await RecordDecisionBestEffortAsync(
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
                await MarkIndeterminateIgnoringFailureAsync(lease, "budget_denial_receipt_uncertain").ConfigureAwait(false);
                return Indeterminate("AGENT_TOOL_BUDGET_DENIALLY_UNCERTAIN");
            }
            await AbandonUnrecordedLeaseBestEffortAsync(lease, "budget_denied").ConfigureAwait(false);
            return !recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required
                ? GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE")
                : decisionOutcome;
        }

        if (reserved.Status != AgentToolBudgetReserveStatus.Reserved
            || reserved.Reservation is not { State: AgentToolBudgetReservationState.Reserved } reservation
            || !Matches(reservation, governance))
        {
            const string reason = "budget_reservation_invalid";
            var decisionOutcome = Indeterminate("AGENT_TOOL_BUDGET_FAILURE");
            var recorded = await RecordDecisionBestEffortAsync(
                auditContext,
                AgentToolGovernanceDecisionState.Indeterminate,
                decisionOutcome,
                reason,
                reserved.Reservation).ConfigureAwait(false);
            if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                decisionOutcome = Indeterminate("decision_audit_failure");
            await MarkIndeterminateIgnoringFailureAsync(lease, reason).ConfigureAwait(false);
            return decisionOutcome;
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
                await MarkIndeterminateIgnoringFailureAsync(lease, "bind_reservation_uncertain")
                    .ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
                return await FinishFenceIndeterminateAsync(
                    null, auditContext, lease, reservation, "bind_reservation_uncertain")
                    .ConfigureAwait(false);
        }

        // Validate Bind returned the expected Ready state.
        if (preDispatchState.State != AgentToolInvocationPreDispatchState.Ready)
        {
            return await FinishFenceIndeterminateAsync(
                null, auditContext, lease, reservation,
                $"bind_reservation_unexpected_state:{preDispatchState.State}")
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
                _ = await ReleaseAuditedBeforeDispatchAsync(
                    entry,
                    auditHandle,
                    auditContext,
                    lease,
                    reservation,
                    "pre_dispatch_cancelled").ConfigureAwait(false);
            }
            else
            {
                _ = await FinishFenceIndeterminateAsync(
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
                return await FinishFenceIndeterminateAsync(
                    null, auditContext, lease, reservation, "pre_dispatch_audit_uncertain")
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
                await MarkIndeterminateIgnoringFailureAsync(lease, "bind_accepted_uncertain")
                    .ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            preDispatchState = await RecoverPreDispatchStateAsync(identity, lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (preDispatchState is null)
                return await FinishFenceIndeterminateAsync(
                    auditHandle, auditContext, lease, reservation, "bind_accepted_uncertain")
                    .ConfigureAwait(false);
        }

        // Validate BindAccepted returned the expected Accepted state.
        if (preDispatchState.State != AgentToolInvocationPreDispatchState.Accepted)
        {
            return await FinishFenceIndeterminateAsync(
                auditHandle, auditContext, lease, reservation,
                $"bind_accepted_unexpected_state:{preDispatchState.State}")
                .ConfigureAwait(false);
        }

        if (renewal.HasFailed)
        {
            return await ReleaseAuditedBeforeDispatchAsync(
                entry,
                auditHandle,
                auditContext,
                lease,
                reservation,
                "lease_renewal_failure").ConfigureAwait(false);
        }

        bool dispatchStarted;
        try
        {
            dispatchStarted = await _invocations.TryMarkDispatchStartedAsync(
                    lease,
                    auditHandle!,
                    reservation.ReservationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return await FinishFenceIndeterminateAsync(
                auditHandle,
                auditContext,
                lease,
                reservation,
                "dispatch_fence_uncertain").ConfigureAwait(false);
        }
        if (!dispatchStarted)
        {
            return await ReleaseAuditedBeforeDispatchAsync(
                entry,
                auditHandle,
                auditContext,
                lease,
                reservation,
                "dispatch_fence_rejected").ConfigureAwait(false);
        }

        var factBuffer = new AgentToolInvocationFactBuffer();
        var preflightReceipts = new AgentToolOutputPreflightReceiptSink(
            entry.PreparedOutcomeContract, entry.OutputAuditProjection, factBuffer);
        CapabilityExecutionResult capabilityResult;
        try
        {
            capabilityResult = await _dispatcher.DispatchAsync(
                entry.Capability,
                InvocationSource.Agent,
                input,
                context => ConfigureCapabilityContext(
                    context, entry, execution, arguments, fingerprint, lease, approval, reservation, factBuffer, preflightReceipts),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return await FinishIndeterminateAsync(
                auditHandle, auditContext, lease, reservation, "dispatcher_failure").ConfigureAwait(false);
        }

        if (renewal.HasFailed || capabilityResult.Status == CapabilityExecutionStatus.TimedOut)
        {
            return await FinishIndeterminateAsync(
                auditHandle,
                auditContext,
                lease,
                reservation,
                renewal.HasFailed ? "lease_renewal_failure" : "capability_timeout").ConfigureAwait(false);
        }

        AgentToolInvocationOutcome outcome;
        IReadOnlyList<AgentToolAuditFact> preparedOutputFacts = Array.Empty<AgentToolAuditFact>();
        if (!capabilityResult.IsSuccess)
        {
            // Once a handler has published a write-before-mutation receipt set,
            // an exception is no longer an ordinary capability failure. The
            // domain may have committed before the exception was observed, so
            // preserve the invocation fence instead of claiming Completed.
            if (preflightReceipts.HasPublishedOutcomes)
            {
                return await FinishIndeterminateAsync(
                    auditHandle, auditContext, lease, reservation, "output_finalization_failure")
                    .ConfigureAwait(false);
            }
            outcome = _results.CapabilityFailure(capabilityResult);
        }
        else
        {
            try
            {
                outcome = await MapSuccessAsync(entry, capabilityResult.Output, cancellationToken)
                    .ConfigureAwait(false);
                preparedOutputFacts = ValidatePreflightReceipt(entry, capabilityResult.Output, outcome, preflightReceipts.Seal());
            }
            catch
            {
                return await FinishIndeterminateAsync(
                    auditHandle, auditContext, lease, reservation, "output_finalization_failure")
                    .ConfigureAwait(false);
            }
        }

        var facts = Array.Empty<AgentToolAuditFact>();
        if (capabilityResult.IsSuccess)
        {
            var snapshot = factBuffer.Seal();
            facts = snapshot.Facts.Concat(preparedOutputFacts).ToArray();
            if (!AgentToolAuditFactValidator.Validate(
                    facts,
                    Math.Min(64, snapshot.MaximumFacts),
                    entry.OutputAuditProjection))
                return await FinishIndeterminateAsync(
                    auditHandle, auditContext, lease, reservation, "audit_fact_limit_violation")
                    .ConfigureAwait(false);
        }
        return await FinishCompletedAsync(
            entry,
            auditHandle,
            auditContext,
            lease,
            reservation,
            outcome,
            facts).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationOutcome> MapSuccessAsync(
        AgentToolRuntimeEntry entry,
        object? output,
        CancellationToken cancellationToken)
    {
        if (entry.OutputSchema is null)
        {
            return output is null
                ? new AgentToolInvocationOutcome
                {
                    Kind = AgentToolInvocationOutcomeKind.Succeeded,
                    Code = "AGENT_TOOL_SUCCEEDED",
                    Message = "The tool completed successfully."
                }
                : ContractFailure("AGENT_TOOL_UNEXPECTED_OUTPUT");
        }
        if (output is null)
            return ContractFailure("AGENT_TOOL_MISSING_OUTPUT");

        JsonElement? serialized;
        try
        {
            serialized = await entry.Binding.Contract.SerializeOutputAsync(
                output,
                entry.Binding.OutputTypeInfo,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return ContractFailure("AGENT_TOOL_OUTPUT_TYPE_MISMATCH");
        }
        if (!serialized.HasValue)
            return ContractFailure("AGENT_TOOL_MISSING_OUTPUT");

        var validation = _schemas.Validate(
            entry.OutputSchema,
            serialized.Value,
            _schemaRegistry?.GetAll() ?? Array.Empty<SchemaDescriptor>(),
            rejectUnknownProperties: true);
        if (!validation.IsValid)
            return ContractFailure("AGENT_TOOL_OUTPUT_SCHEMA_VIOLATION");

        return new AgentToolInvocationOutcome
        {
            Kind = AgentToolInvocationOutcomeKind.Succeeded,
            Code = "AGENT_TOOL_SUCCEEDED",
            Message = "The tool completed successfully.",
            StructuredOutput = serialized.Value.Clone()
        };
    }

    private static IReadOnlyList<AgentToolAuditFact> ValidatePreflightReceipt(
        AgentToolRuntimeEntry entry,
        object? typedOutput,
        AgentToolInvocationOutcome outcome,
        IReadOnlyList<AgentToolPreparedOutcomeReceipt> receipts)
    {
        if (entry.PreparedOutcomeContract is not null && receipts.Count == 0)
            throw new InvalidOperationException("The tool did not publish its required prepared outcome set.");
        if (receipts.Count == 0)
            return Array.Empty<AgentToolAuditFact>();
        if (outcome.StructuredOutput is not { } structured)
            throw new InvalidOperationException("Preflight receipt requires structured output.");

        var outputHash = ComputeStructuredOutputHash(structured);
        var contractFingerprint = entry.OutputSchemaContractHash ?? entry.ToolContractHash;
        var outcomeCode = entry.OutputOutcomeCodeProjector?.Invoke(typedOutput);
        if (entry.PreparedOutcomeContract is not null && outcomeCode is null)
            throw new InvalidOperationException("The prepared outcome contract has no typed outcome discriminator.");
        var matches = receipts.Where(item =>
            outcomeCode is not null
            && string.Equals(item.OutcomeCode, outcomeCode, StringComparison.Ordinal)
            && string.Equals(item.Receipt.ToolDescriptorId, entry.Descriptor.Id, StringComparison.Ordinal)
            && item.Receipt.ToolDescriptorVersion == entry.Descriptor.Version
            && string.Equals(item.Receipt.OutputContractFingerprint, contractFingerprint, StringComparison.Ordinal)
            && string.Equals(item.Receipt.StructuredOutputHash, outputHash, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Final output did not match exactly one preflight receipt.");
            var projected = entry.OutputAuditProjector?.Invoke(typedOutput) ?? Array.Empty<AgentToolAuditFact>();
        if (!projected.SequenceEqual(matches[0].ProjectedOutputFacts))
            throw new InvalidOperationException("Final output audit facts did not match the prepared outcome proof.");
        return matches[0].InternalFacts.Concat(projected).ToArray();
    }

    private static string ComputeStructuredOutputHash(JsonElement output)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(output.GetRawText())))
            .ToLowerInvariant();

    private async ValueTask<AgentToolInvocationOutcome> FinishCompletedAsync(
        AgentToolRuntimeEntry entry,
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        AgentToolInvocationOutcome outcome,
        IReadOnlyList<AgentToolAuditFact> auditFacts)
    {
        AgentToolBudgetReservation settled;
        try
        {
            settled = await FinalizeBudgetAsync(reservation, AgentToolBudgetReservationState.Committed, "dispatch_completed")
                .ConfigureAwait(false);
        }
        catch
        {
            return await FinishIndeterminateWithoutBudgetAsync(
                auditHandle, auditContext, lease, reservation, "budget_settlement_failure").ConfigureAwait(false);
        }

        try
        {
            await _invocations.PrepareCompletionAsync(
                    lease,
                    new AgentToolInvocationPrepareCompletionRequest
                    {
                        Outcome = outcome,
                        AuditId = auditHandle?.AuditId,
                        BudgetReservationId = settled.ReservationId,
                        ReasonCode = "dispatch_completed"
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            return await FinishIndeterminateWithSettledBudgetAsync(
                auditHandle,
                auditContext,
                lease,
                settled,
                "invocation_completion_failure").ConfigureAwait(false);
        }

        if (auditHandle is not null)
        {
            var completedFinalization = Finalization(
                auditHandle, auditContext, lease, settled, true,
                AgentToolGovernanceAttemptFinalState.Completed,
                AgentToolInvocationTerminalState.Completed,
                outcome,
                "dispatch_completed",
                auditFacts);
            var confirmation = await ConfirmAuditFinalizationAsync(
                completedFinalization,
                entry.Governance.EffectiveAuditMode).ConfigureAwait(false);

            if (confirmation == AgentToolAuditConfirmation.Indeterminate)
            {
                await MarkIndeterminateIgnoringFailureAsync(
                    lease,
                    "post_dispatch_audit_indeterminate").ConfigureAwait(false);
                return Indeterminate("post_dispatch_audit_indeterminate");
            }

            if (confirmation == AgentToolAuditConfirmation.Conflict)
                return Indeterminate("post_dispatch_audit_conflict");

            if (entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required
                && confirmation != AgentToolAuditConfirmation.Completed)
                return Indeterminate("post_dispatch_audit_uncertain");
        }

        try
        {
            var published = await _invocations.PublishCompletionAsync(lease, CancellationToken.None)
                .ConfigureAwait(false);
            if (MatchesPreparedCompletion(
                    published,
                    outcome,
                    auditHandle?.AuditId,
                    settled.ReservationId,
                    "dispatch_completed"))
                return outcome;

            return await ResolvePublishUncertaintyAsync(
                lease,
                outcome,
                auditHandle?.AuditId,
                settled.ReservationId,
                "dispatch_completed").ConfigureAwait(false);
        }
        catch
        {
            return await ResolvePublishUncertaintyAsync(
                lease,
                outcome,
                auditHandle?.AuditId,
                settled.ReservationId,
                "dispatch_completed").ConfigureAwait(false);
        }
    }

    private async ValueTask<AgentToolInvocationOutcome> ResolvePublishUncertaintyAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationOutcome expectedOutcome,
        string? expectedAuditId,
        string expectedReservationId,
        string expectedReasonCode)
    {
        try
        {
            var state = await _invocations.GetCompletionStateAsync(lease, CancellationToken.None)
                .ConfigureAwait(false);
            return MatchesPreparedCompletion(
                    state,
                    expectedOutcome,
                    expectedAuditId,
                    expectedReservationId,
                    expectedReasonCode)
                ? expectedOutcome
                : Indeterminate("invocation_publish_uncertain");
        }
        catch
        {
            return Indeterminate("invocation_publish_uncertain");
        }
    }

    private async ValueTask<AgentToolAuditConfirmation> QueryAuditConfirmationAsync(
        string auditId,
        AgentToolGovernanceFinalizationRecord expected)
    {
        try
        {
            var result = await _audit.GetFinalizationStateAsync(auditId, CancellationToken.None)
                .ConfigureAwait(false);
            return ResolveAuditConfirmation(result, expected);
        }
        catch
        {
            return AgentToolAuditConfirmation.Unconfirmed;
        }
    }

    private async ValueTask<AgentToolAuditConfirmation> ConfirmAuditFinalizationAsync(
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

    private static AgentToolAuditConfirmation ResolveAuditConfirmation(
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

    private static bool MatchesPreparedCompletion(
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

    private static bool EquivalentFinalization(
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

    private static bool EquivalentContext(
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

    private static bool SameFinalizationIdentity(
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

    private enum AgentToolAuditConfirmation
    {
        Unconfirmed,
        Completed,
        Indeterminate,
        Conflict
    }

    private static bool EquivalentOutcome(
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

    private async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateWithSettledBudgetAsync(
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

    private async ValueTask<AgentToolInvocationOutcome> FinalizeIndeterminateAfterGateAsync(
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

    private async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateAsync(
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        string reasonCode)
    {
        AgentToolBudgetReservation settled;
        try
        {
            settled = await FinalizeBudgetAsync(reservation, AgentToolBudgetReservationState.Indeterminate, reasonCode)
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

    private async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateWithoutBudgetAsync(
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

    private async ValueTask<AgentToolInvocationOutcome> ReleaseAuditedBeforeDispatchAsync(
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
            released = await FinalizeBudgetAsync(reservation, AgentToolBudgetReservationState.Released, reasonCode)
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

    private async ValueTask<AgentToolInvocationOutcome> ResolveReleaseUncertaintyAsync(
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

    private static bool MatchesPublishedRelease(
        AgentToolInvocationReleaseResult result,
        string? auditId,
        string reservationId,
        string reasonCode)
        => result.State == AgentToolInvocationReleaseState.Released
            && result.PreparedAt.HasValue
            && string.Equals(result.AuditId, auditId, StringComparison.Ordinal)
            && string.Equals(result.BudgetReservationId, reservationId, StringComparison.Ordinal)
            && string.Equals(result.ReasonCode, reasonCode, StringComparison.Ordinal);

    private async ValueTask<AgentToolInvocationOutcome> FinishFenceIndeterminateAsync(
        AgentToolGovernancePreDispatchReceipt? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        string reasonCode)
    {
        AgentToolBudgetReservation settled;
        try
        {
            // This worker has not called Dispatcher, so business budget may be
            // released even though the durable fencing transition is unknown.
            settled = await FinalizeBudgetAsync(
                    reservation,
                    AgentToolBudgetReservationState.Released,
                    reasonCode)
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

    private ValueTask<AgentToolBudgetReservation> FinalizeBudgetAsync(
        AgentToolBudgetReservation reservation,
        AgentToolBudgetReservationState state,
        string reasonCode)
        => _budget.FinalizeAsync(
            new AgentToolBudgetFinalizeRequest
            {
                ReservationId = reservation.ReservationId,
                AttemptId = reservation.AttemptId,
                InvocationFingerprint = reservation.InvocationFingerprint,
                RequestedState = state,
                ReasonCode = reasonCode
            },
            CancellationToken.None);

    private void ConfigureCapabilityContext(
        CapabilityExecutionContext context,
        AgentToolRuntimeEntry entry,
        AgentExecutionContext execution,
        JsonElement arguments,
        AgentToolInvocationFingerprint fingerprint,
        AgentToolInvocationLease lease,
        AgentToolApprovalResult approval,
        AgentToolBudgetReservation reservation,
        IAgentToolInvocationFactBufferOwner factBuffer,
        AgentToolOutputPreflightReceiptSink preflightReceipts)
    {
        context.CausationId = execution.CausationId;
        context.AccountabilityActor = new AuditActor
        {
            Kind = "agent",
            Id = execution.AgentId,
            InitiatedBy = string.IsNullOrWhiteSpace(context.UserId)
                ? null
                : new AuditActorReference("user", context.UserId)
        };
        context.AccountabilityRuntimeReferences = ImmutableArray.Create(
            new AuditRuntimeReference("agent-session", execution.ExecutionId),
            new AuditRuntimeReference("agent-invocation", execution.InvocationId));
        context.IdempotencyKey = _idempotency.Build(fingerprint);
        context.InputJson = arguments.Clone();
        context.Items[AgentCapabilityContextItemNames.ToolDescriptorId] = entry.Descriptor.Id;
        context.Items[AgentCapabilityContextItemNames.ToolDescriptorVersion] = entry.Descriptor.Version;
        context.Items[AgentCapabilityContextItemNames.ToolName] = entry.Descriptor.ToolName;
        context.Items[AgentCapabilityContextItemNames.AgentId] = execution.AgentId;
        context.Items[AgentCapabilityContextItemNames.ExecutionId] = execution.ExecutionId;
        context.Items[AgentCapabilityContextItemNames.InvocationId] = execution.InvocationId;
        context.Items[AgentCapabilityContextItemNames.CallOrigin] = execution.CallOrigin;
        context.Items[AgentCapabilityContextItemNames.AttemptId] = lease.AttemptId;
        context.Items[AgentCapabilityContextItemNames.ApprovalEvidenceId] = approval.EvidenceId;
        context.Items[AgentCapabilityContextItemNames.BudgetReservationId] = reservation.ReservationId;
        context.Items[AgentCapabilityContextItemNames.OutputSchemaContractFingerprint] =
            entry.OutputSchemaContractHash ?? entry.ToolContractHash;
        if (entry.OutputAuditProjection is not null)
            context.Items[AgentCapabilityContextItemNames.OutputAuditProjectionContract] = entry.OutputAuditProjection;
        if (entry.OutputSchema is not null)
        {
            context.Items[AgentCapabilityContextItemNames.OutputPreflightRuntime] =
                new AgentToolOutputPreflightRuntime(
                    entry.Descriptor.Id,
                    entry.Descriptor.Version,
                    entry.OutputSchemaContractHash ?? entry.ToolContractHash,
                    entry.Binding.Contract.OutputType
                        ?? throw new InvalidOperationException("Output binding type is missing."),
                    entry.Binding.OutputTypeInfo
                        ?? throw new InvalidOperationException("Output binding JsonTypeInfo is missing."),
                    entry.OutputSchema,
                    _schemaRegistry?.GetAll() ?? Array.Empty<SchemaDescriptor>(),
                    _schemas,
                    entry.OutputAuditProjector,
                    entry.OutputAuditProjection);
        }
        context.Items[AgentCapabilityContextItemNames.InvocationBindingSnapshot] =
            new AgentToolInvocationBindingSnapshot
            {
                LogicalKey = new AgentToolLogicalInvocationKey(
                    context.TenantId,
                    _currentUser.Id,
                    execution.AgentId,
                    execution.ExecutionId,
                    execution.InvocationId),
                InvocationFingerprint = fingerprint.Value
            };
        context.Items[AgentCapabilityContextItemNames.InvocationFactBuffer] = (IAgentToolInvocationFactSink)factBuffer;
        context.Items[AgentCapabilityContextItemNames.OutputPreflightReceiptSink] = preflightReceipts;
    }

    private static AgentToolGovernanceContext CreateGovernanceContext(
        AgentToolRuntimeEntry entry,
        AgentExecutionContext execution,
        AgentToolLogicalInvocationKey key,
        AgentToolInvocationLease lease,
        AgentToolInvocationFingerprint fingerprint)
        => CreateGovernanceContext(entry, execution, key, lease.AttemptId, fingerprint);

    private static AgentToolGovernanceContext CreateGovernanceContext(
        AgentToolRuntimeEntry entry,
        AgentExecutionContext execution,
        AgentToolLogicalInvocationKey key,
        string attemptId,
        AgentToolInvocationFingerprint fingerprint,
        bool argumentsEvaluated = true)
        => new()
        {
            LogicalInvocationKey = key,
            AttemptId = attemptId,
            InvocationFingerprint = fingerprint.Value,
            ArgumentsHash = argumentsEvaluated ? fingerprint.ArgumentsHash : null,
            ArgumentsEvaluated = argumentsEvaluated,
            ExecutionContext = execution,
            ToolContract = entry.DiscoveryContract.ToolContract,
            CapabilityContract = entry.DiscoveryContract.CapabilityContract,
            InputSchemaContract = entry.DiscoveryContract.InputSchemaContract,
            OutputSchemaContract = entry.DiscoveryContract.OutputSchemaContract,
            Governance = entry.Governance
        };

    private static AgentToolInvocationFingerprint NotEvaluatedFingerprint()
        => new("not-evaluated", "decision-not-evaluated");

    private AgentToolInvocationFingerprint RawArgumentsFingerprint(JsonElement? arguments)
    {
        var hash = _fingerprints.BuildRawArgumentsHash(arguments);
        return new AgentToolInvocationFingerprint(hash, $"decision-raw-{hash}");
    }

    private AgentToolInvocationFingerprint TryBuildDecisionFingerprint(
        AgentToolRuntimeEntry entry,
        AgentExecutionContext execution,
        AgentToolLogicalInvocationKey key,
        JsonElement arguments)
    {
        try
        {
            return _fingerprints.Build(entry, execution, key, arguments);
        }
        catch (Exception) when (arguments.ValueKind == JsonValueKind.Object)
        {
            return RawArgumentsFingerprint(arguments);
        }
    }

    private async ValueTask<AgentToolInvocationOutcome> RecordDecisionAndReturnAsync(
        AgentToolGovernanceContext context,
        AgentToolGovernanceDecisionState decision,
        AgentToolInvocationOutcome outcome,
        string reasonCode,
        AgentToolBudgetReservation? observedReservation = null)
    {
        var recorded = await RecordDecisionBestEffortAsync(
            CreateAuditContext(context), decision, outcome, reasonCode, observedReservation)
            .ConfigureAwait(false);
        if (recorded || context.Governance.EffectiveAuditMode != AgentToolAuditMode.Required)
            return outcome;

        if (outcome.Kind == AgentToolInvocationOutcomeKind.UnknownTool)
            return outcome;

        return decision == AgentToolGovernanceDecisionState.Indeterminate
            ? Indeterminate("decision_audit_failure")
            : GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE");
    }

    private async ValueTask<bool> RecordDecisionBestEffortAsync(
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

    private async ValueTask<AgentToolBudgetReservationReadResult?> RecoverReservationStateAsync(
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

    private async ValueTask<AgentToolGovernancePreDispatchReceipt?> RecoverAuditReceiptAsync(
        AgentToolGovernancePreDispatchRecord record,
        CancellationToken cancellationToken)
    {
        // Authoritative recovery: query the auditor for the persisted state first.
        var identity = new AgentToolPreDispatchIdentity(record.Context.LogicalInvocationKey, record.Context.AttemptId);
        try
        {
            var readResult = await _audit.GetPreDispatchStateAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            if (readResult.Status == AgentToolGovernancePreDispatchReadStatus.Accepted
                && readResult.Receipt is not null)
            {
                return readResult.Receipt;
            }
        }
        catch
        {
            // Authority unavailable — fall through to write retry.
        }

        // Authority says Missing or was unavailable — retry the write to establish the checkpoint.
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

    private async ValueTask<AgentToolInvocationPreDispatchResult?> RecoverPreDispatchStateAsync(
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

    private async ValueTask<bool> PublishBudgetDenialBestEffortAsync(
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

    private static AgentToolGovernanceAuditContext CreateAuditContext(AgentToolGovernanceContext context)
        => new()
        {
            LogicalInvocationKey = context.LogicalInvocationKey,
            AttemptId = context.AttemptId,
            InvocationFingerprint = context.InvocationFingerprint,
            ArgumentsHash = context.ArgumentsHash,
            ArgumentsEvaluated = context.ArgumentsEvaluated,
            CallOrigin = context.ExecutionContext.CallOrigin,
            AgentRolesHash = HashRoles(context.ExecutionContext.AgentRoles),
            ToolContract = context.ToolContract,
            CapabilityContract = context.CapabilityContract,
            InputSchemaContract = context.InputSchemaContract,
            OutputSchemaContract = context.OutputSchemaContract,
            Governance = context.Governance
        };

    private static AgentToolGovernanceFinalizationRecord Finalization(
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

    private bool TryGetTrustedIdentity(out string? tenantId, out string userId)
    {
        tenantId = NormalizeTenant(_tenant.CurrentTenantId);
        userId = _currentUser.Id;
        return _currentUser.IsAuthenticated
            && !string.IsNullOrWhiteSpace(userId)
            && string.Equals(tenantId, NormalizeTenant(_currentUser.TenantId), StringComparison.Ordinal);
    }

    private static string? NormalizeTenant(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;

    private static bool TryNormalizeArguments(
        JsonElement? supplied,
        out JsonElement arguments,
        out AgentToolInvocationOutcome? failure)
    {
        if (!supplied.HasValue)
        {
            using var empty = JsonDocument.Parse("{}");
            arguments = empty.RootElement.Clone();
            failure = null;
            return true;
        }
        if (supplied.Value.ValueKind != JsonValueKind.Object)
        {
            arguments = default;
            failure = Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_ARGUMENTS_NOT_OBJECT", "Tool arguments must be an object.");
            return false;
        }
        arguments = supplied.Value.Clone();
        failure = null;
        return true;
    }

    private static bool HasDuplicateProperties(JsonElement arguments)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return arguments.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static bool IsAcceptedApproval(AgentToolApprovalResult? approval)
        => approval is
            { Decision: AgentToolApprovalDecision.NotRequired, ClaimState: AgentToolApprovalEvidenceClaimState.NotApplicable }
            or { Decision: AgentToolApprovalDecision.Approved, ClaimState: AgentToolApprovalEvidenceClaimState.Claimed, EvidenceId.Length: > 0 };

    private static bool Matches(AgentToolBudgetReservation reservation, AgentToolGovernanceContext context)
        => string.Equals(reservation.AttemptId, context.AttemptId, StringComparison.Ordinal)
            && string.Equals(reservation.InvocationFingerprint, context.InvocationFingerprint, StringComparison.Ordinal)
            && string.Equals(reservation.Category, context.Governance.Budget.Category, StringComparison.Ordinal)
            && reservation.CostUnits == context.Governance.Budget.CostUnits
            && reservation.MaxCallsPerExecution == context.Governance.Budget.MaxCallsPerExecution;

    private static AgentToolInvocationOutcome MapAcquire(AgentToolInvocationAcquireResult result)
        => result.Status switch
        {
            AgentToolInvocationAcquireStatus.Completed when result.CompletedOutcome is not null => result.CompletedOutcome,
            AgentToolInvocationAcquireStatus.InProgress => Outcome(AgentToolInvocationOutcomeKind.InProgress, "AGENT_TOOL_INVOCATION_IN_PROGRESS", "The same tool invocation is already in progress."),
            AgentToolInvocationAcquireStatus.Conflict => Outcome(AgentToolInvocationOutcomeKind.InvocationConflict, "AGENT_TOOL_INVOCATION_CONFLICT", "The invocation identity is already bound to a different request."),
            AgentToolInvocationAcquireStatus.Indeterminate => Indeterminate("logical_invocation_indeterminate"),
            _ => Outcome(AgentToolInvocationOutcomeKind.InternalServer, "AGENT_TOOL_GATE_INVALID_RESULT", "The tool could not be started.")
        };

    private static string HashRoles(IReadOnlySet<string> roles)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var role in roles.OrderBy(role => role, StringComparer.Ordinal))
                writer.WriteStringValue(role);
            writer.WriteEndArray();
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static AgentToolInvocationOutcome Outcome(
        AgentToolInvocationOutcomeKind kind,
        string code,
        string message)
        => AgentToolResultMapper.Outcome(kind, code, message);

    private static AgentToolInvocationOutcome GovernanceDenied(string code)
        => Outcome(AgentToolInvocationOutcomeKind.GovernanceDenied, code, "The tool invocation was blocked by governance policy.");

    private static AgentToolInvocationOutcome ContractFailure(string code)
        => Outcome(AgentToolInvocationOutcomeKind.InternalContractFailure, code, "The tool produced an invalid server result.");

    private static AgentToolInvocationOutcome Indeterminate(string reasonCode)
        => Outcome(AgentToolInvocationOutcomeKind.InvocationIndeterminate, "AGENT_TOOL_INVOCATION_INDETERMINATE", "The invocation result is uncertain and must not be retried automatically.");

    private async ValueTask AbandonUnrecordedLeaseBestEffortAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        _ = await TryAbandonUnrecordedLeaseAsync(lease, reasonCode).ConfigureAwait(false);
    }

    private async ValueTask<bool> TryAbandonUnrecordedLeaseAsync(
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

    private async ValueTask<AgentToolInvocationOutcome> MarkIndeterminateBestEffortAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        await MarkIndeterminateIgnoringFailureAsync(lease, reasonCode).ConfigureAwait(false);
        return Indeterminate(reasonCode);
    }

    private async ValueTask MarkIndeterminateIgnoringFailureAsync(
        AgentToolInvocationLease lease,
        string reasonCode)
    {
        _ = await TryMarkIndeterminateAsync(lease, reasonCode).ConfigureAwait(false);
    }

    private async ValueTask<bool> TryMarkIndeterminateAsync(
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

    private sealed class LeaseRenewal : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private Exception? _failure;

        public LeaseRenewal(IAgentToolInvocationGate gate, AgentToolInvocationLease lease)
        {
            var duration = lease.ExpiresAt - lease.AcquiredAt;
            var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(10).Ticks, duration.Ticks / 3));
            _loop = RunAsync(gate, lease, interval);
        }

        public bool HasFailed => Volatile.Read(ref _failure) is not null;

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            _stop.Dispose();
        }

        private async Task RunAsync(
            IAgentToolInvocationGate gate,
            AgentToolInvocationLease lease,
            TimeSpan interval)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(interval, _stop.Token).ConfigureAwait(false);
                    _ = await gate.RenewAsync(lease, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _failure, exception);
            }
        }
    }
}
