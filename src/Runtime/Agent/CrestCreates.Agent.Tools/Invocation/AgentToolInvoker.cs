using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
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
    private readonly IAgentToolApprovalGate _approval;
    private readonly IAgentToolBudgetGate _budget;
    private readonly IAgentToolGovernanceAuditor _audit;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly ISchemaValidator _schemas;
    private readonly AgentToolInvocationFingerprintBuilder _fingerprints;
    private readonly AgentCapabilityIdempotencyKeyBuilder _idempotency;
    private readonly AgentToolResultMapper _results;

    public AgentToolInvoker(
        AgentToolRuntimeSnapshotProvider snapshots,
        IAgentExecutionContextAccessor execution,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IAgentToolInvocationGate invocations,
        IAgentToolApprovalGate approval,
        IAgentToolBudgetGate budget,
        IAgentToolGovernanceAuditor audit,
        ICapabilityDispatcher dispatcher,
        ISchemaValidator schemas,
        AgentToolInvocationFingerprintBuilder fingerprints,
        AgentCapabilityIdempotencyKeyBuilder idempotency,
        AgentToolResultMapper results)
    {
        _snapshots = snapshots;
        _execution = execution;
        _currentUser = currentUser;
        _tenant = tenant;
        _invocations = invocations;
        _approval = approval;
        _budget = budget;
        _audit = audit;
        _dispatcher = dispatcher;
        _schemas = schemas;
        _fingerprints = fingerprints;
        _idempotency = idempotency;
        _results = results;
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
        if (entry is null || !AgentToolCatalog.IsVisible(entry, execution!))
            return Outcome(AgentToolInvocationOutcomeKind.UnknownTool, "AGENT_TOOL_UNKNOWN", "The requested tool is unavailable.");

        if (!TryNormalizeArguments(request.Arguments, out var arguments, out var argumentFailure))
            return argumentFailure!;
        if (HasDuplicateProperties(arguments))
            return Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_DUPLICATE_ARGUMENT", "Tool arguments contain duplicate properties.");
        if (entry.InputSchema is not null)
        {
            var validation = _schemas.Validate(entry.InputSchema, arguments, rejectUnknownProperties: true);
            if (!validation.IsValid)
            {
                return new AgentToolInvocationOutcome
                {
                    Kind = AgentToolInvocationOutcomeKind.InvalidRequest,
                    Code = "AGENT_TOOL_INVALID_ARGUMENTS",
                    Message = "Tool arguments are invalid.",
                    Issues = validation.Errors.Select(error =>
                        new AgentToolInvocationIssue(error.ErrorCode, error.FieldName)).ToArray()
                };
            }
        }
        else if (arguments.EnumerateObject().Any())
        {
            return Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_ARGUMENTS_NOT_ACCEPTED", "This tool does not accept arguments.");
        }

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
            return Outcome(AgentToolInvocationOutcomeKind.InvalidRequest, "AGENT_TOOL_INVALID_ARGUMENTS", "Tool arguments are invalid.");
        }

        var key = new AgentToolLogicalInvocationKey(
            tenantId,
            userId,
            execution!.AgentId,
            execution.ExecutionId,
            execution.InvocationId);
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
            await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
            return GovernanceDenied("AGENT_TOOL_APPROVAL_FAILURE");
        }

        if (!IsAcceptedApproval(approval))
        {
            await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
            return GovernanceDenied("AGENT_TOOL_APPROVAL_DENIED");
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
            await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
            return GovernanceDenied("AGENT_TOOL_BUDGET_FAILURE");
        }

        if (reserved.Status != AgentToolBudgetReserveStatus.Reserved
            || reserved.Reservation is not { State: AgentToolBudgetReservationState.Reserved } reservation
            || !Matches(reservation, governance))
        {
            await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
            return GovernanceDenied("AGENT_TOOL_BUDGET_DENIED");
        }

        var auditContext = CreateAuditContext(governance);
        AgentToolGovernanceAuditHandle? auditHandle = null;
        try
        {
            auditHandle = await _audit.RecordPreDispatchAsync(
                new AgentToolGovernancePreDispatchRecord
                {
                    Context = auditContext,
                    Lease = lease,
                    Approval = approval,
                    BudgetReservation = reservation
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseBeforeDispatchAsync(lease, reservation, "pre_dispatch_cancelled").ConfigureAwait(false);
            throw;
        }
        catch
        {
            if (entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
            {
                await ReleaseBeforeDispatchAsync(lease, reservation, "pre_dispatch_audit_failure").ConfigureAwait(false);
                return GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE");
            }
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
            dispatchStarted = await _invocations.TryMarkDispatchStartedAsync(lease, cancellationToken)
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

        CapabilityExecutionResult capabilityResult;
        try
        {
            capabilityResult = await _dispatcher.DispatchAsync(
                entry.Capability,
                InvocationSource.Agent,
                input,
                context => ConfigureCapabilityContext(
                    context, entry, execution, arguments, lease, approval, reservation),
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
        if (!capabilityResult.IsSuccess)
        {
            outcome = _results.CapabilityFailure(capabilityResult);
        }
        else
        {
            try
            {
                outcome = await MapSuccessAsync(entry, capabilityResult.Output, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return await FinishIndeterminateAsync(
                    auditHandle, auditContext, lease, reservation, "output_finalization_failure")
                    .ConfigureAwait(false);
            }
        }

        return await FinishCompletedAsync(
            entry,
            auditHandle,
            auditContext,
            lease,
            reservation,
            outcome).ConfigureAwait(false);
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

        var validation = _schemas.Validate(entry.OutputSchema, serialized.Value, rejectUnknownProperties: true);
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

    private async ValueTask<AgentToolInvocationOutcome> FinishCompletedAsync(
        AgentToolRuntimeEntry entry,
        AgentToolGovernanceAuditHandle? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        AgentToolInvocationOutcome outcome)
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

        if (auditHandle is not null)
        {
            try
            {
                await _audit.FinalizeAsync(
                    Finalization(
                        auditHandle, auditContext, lease, settled, true,
                        AgentToolGovernanceAttemptFinalState.Completed,
                        AgentToolInvocationTerminalState.Completed,
                        outcome,
                        "dispatch_completed"),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                {
                    return await MarkIndeterminateBestEffortAsync(lease, "post_dispatch_audit_failure")
                        .ConfigureAwait(false);
                }
            }
        }

        try
        {
            await _invocations.CompleteAsync(lease, outcome, CancellationToken.None).ConfigureAwait(false);
            return outcome;
        }
        catch
        {
            return await MarkIndeterminateBestEffortAsync(lease, "invocation_completion_failure")
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateAsync(
        AgentToolGovernanceAuditHandle? auditHandle,
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

        var outcome = Indeterminate(reasonCode);
        if (auditHandle is not null)
        {
            try
            {
                await _audit.FinalizeAsync(
                    Finalization(
                        auditHandle, auditContext, lease, settled, true,
                        AgentToolGovernanceAttemptFinalState.Indeterminate,
                        AgentToolInvocationTerminalState.Indeterminate,
                        outcome,
                        reasonCode),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                reasonCode = "post_dispatch_audit_failure";
                outcome = Indeterminate(reasonCode);
            }
        }

        await MarkIndeterminateIgnoringFailureAsync(lease, reasonCode).ConfigureAwait(false);
        return outcome;
    }

    private async ValueTask<AgentToolInvocationOutcome> FinishIndeterminateWithoutBudgetAsync(
        AgentToolGovernanceAuditHandle? auditHandle,
        AgentToolGovernanceAuditContext auditContext,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        string reasonCode)
    {
        var outcome = Indeterminate(reasonCode);
        await MarkIndeterminateIgnoringFailureAsync(lease, reasonCode).ConfigureAwait(false);
        return outcome;
    }

    private async ValueTask<AgentToolInvocationOutcome> ReleaseAuditedBeforeDispatchAsync(
        AgentToolRuntimeEntry entry,
        AgentToolGovernanceAuditHandle? auditHandle,
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
            await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
            return GovernanceDenied("AGENT_TOOL_BUDGET_FINALIZATION_FAILURE");
        }

        var releasedLease = await TryReleaseLeaseAsync(lease).ConfigureAwait(false);
        var outcome = releasedLease
            ? Outcome(AgentToolInvocationOutcomeKind.InProgress, "AGENT_TOOL_INVOCATION_NOT_ACQUIRED", "The tool invocation could not acquire execution ownership.")
            : Indeterminate("lease_release_failure");
        if (auditHandle is not null)
        {
            try
            {
                await _audit.FinalizeAsync(
                    Finalization(
                        auditHandle, auditContext, lease, released, false,
                        releasedLease
                            ? AgentToolGovernanceAttemptFinalState.Released
                            : AgentToolGovernanceAttemptFinalState.Indeterminate,
                        releasedLease ? null : AgentToolInvocationTerminalState.Indeterminate,
                        outcome,
                        reasonCode),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                    outcome = GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE");
            }
        }

        return outcome;
    }

    private async ValueTask<AgentToolInvocationOutcome> FinishFenceIndeterminateAsync(
        AgentToolGovernanceAuditHandle? auditHandle,
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
            settled = reservation;
        }

        var outcome = Indeterminate(reasonCode);
        if (auditHandle is not null && settled.State == AgentToolBudgetReservationState.Released)
        {
            try
            {
                await _audit.FinalizeAsync(
                    Finalization(
                        auditHandle,
                        auditContext,
                        lease,
                        settled,
                        false,
                        AgentToolGovernanceAttemptFinalState.Indeterminate,
                        AgentToolInvocationTerminalState.Indeterminate,
                        outcome,
                        reasonCode),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The invocation remains Indeterminate regardless of audit availability.
            }
        }

        await MarkIndeterminateIgnoringFailureAsync(lease, reasonCode).ConfigureAwait(false);
        return outcome;
    }

    private async ValueTask ReleaseBeforeDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        string reasonCode)
    {
        try
        {
            await FinalizeBudgetAsync(reservation, AgentToolBudgetReservationState.Released, reasonCode)
                .ConfigureAwait(false);
        }
        catch
        {
            // Cleanup is best effort; the adapter retains the unresolved reservation for reconciliation.
        }
        await ReleaseLeaseBestEffortAsync(lease).ConfigureAwait(false);
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
        AgentToolInvocationLease lease,
        AgentToolApprovalResult approval,
        AgentToolBudgetReservation reservation)
    {
        context.CausationId = execution.CausationId;
        context.IdempotencyKey = _idempotency.Build(entry, execution);
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
    }

    private static AgentToolGovernanceContext CreateGovernanceContext(
        AgentToolRuntimeEntry entry,
        AgentExecutionContext execution,
        AgentToolLogicalInvocationKey key,
        AgentToolInvocationLease lease,
        AgentToolInvocationFingerprint fingerprint)
        => new()
        {
            LogicalInvocationKey = key,
            AttemptId = lease.AttemptId,
            InvocationFingerprint = fingerprint.Value,
            ArgumentsHash = fingerprint.ArgumentsHash,
            ExecutionContext = execution,
            ToolContract = entry.DiscoveryContract.ToolContract,
            CapabilityContract = entry.DiscoveryContract.CapabilityContract,
            InputSchemaContract = entry.DiscoveryContract.InputSchemaContract,
            OutputSchemaContract = entry.DiscoveryContract.OutputSchemaContract,
            Governance = entry.Governance
        };

    private static AgentToolGovernanceAuditContext CreateAuditContext(AgentToolGovernanceContext context)
        => new()
        {
            LogicalInvocationKey = context.LogicalInvocationKey,
            AttemptId = context.AttemptId,
            InvocationFingerprint = context.InvocationFingerprint,
            ArgumentsHash = context.ArgumentsHash,
            CallOrigin = context.ExecutionContext.CallOrigin,
            AgentRolesHash = HashRoles(context.ExecutionContext.AgentRoles),
            ToolContract = context.ToolContract,
            CapabilityContract = context.CapabilityContract,
            InputSchemaContract = context.InputSchemaContract,
            OutputSchemaContract = context.OutputSchemaContract,
            Governance = context.Governance
        };

    private static AgentToolGovernanceFinalizationRecord Finalization(
        AgentToolGovernanceAuditHandle handle,
        AgentToolGovernanceAuditContext context,
        AgentToolInvocationLease lease,
        AgentToolBudgetReservation reservation,
        bool dispatchStarted,
        AgentToolGovernanceAttemptFinalState attemptState,
        AgentToolInvocationTerminalState? invocationState,
        AgentToolInvocationOutcome outcome,
        string reasonCode)
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

    private async ValueTask ReleaseLeaseBestEffortAsync(AgentToolInvocationLease lease)
    {
        _ = await TryReleaseLeaseAsync(lease).ConfigureAwait(false);
    }

    private async ValueTask<bool> TryReleaseLeaseAsync(AgentToolInvocationLease lease)
    {
        try
        {
            await _invocations.ReleaseLeaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
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
        try
        {
            await _invocations.MarkIndeterminateAsync(lease, reasonCode, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // The durable gate or its reconciler remains authoritative when ownership was lost.
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
