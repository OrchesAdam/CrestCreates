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
    private readonly AgentToolPreDispatchFinalizer _finalizer;
    private readonly AgentToolPreDispatchCoordinator _coordinator;

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
        _finalizer = new AgentToolPreDispatchFinalizer(invocations, budget, audit, leaseAbandoner);
        _coordinator = new AgentToolPreDispatchCoordinator(invocations, budget, audit, _finalizer);
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
            await _finalizer.AbandonUnrecordedLeaseBestEffortAsync(lease, "approval_cancelled").ConfigureAwait(false);
            throw;
        }
        catch
        {
            var decisionOutcome = Indeterminate("AGENT_TOOL_APPROVAL_FAILURE");
            var recorded = await _finalizer.RecordDecisionBestEffortAsync(
                CreateAuditContext(governance),
                AgentToolGovernanceDecisionState.Indeterminate,
                decisionOutcome,
                "approval_failure").ConfigureAwait(false);
            if (!recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required)
                decisionOutcome = Indeterminate("decision_audit_failure");
            await _finalizer.MarkIndeterminateIgnoringFailureAsync(lease, "approval_failure").ConfigureAwait(false);
            return decisionOutcome;
        }

        if (!IsAcceptedApproval(approval))
        {
            var recorded = await _finalizer.RecordDecisionBestEffortAsync(
                CreateAuditContext(governance),
                AgentToolGovernanceDecisionState.Denied,
                GovernanceDenied("AGENT_TOOL_APPROVAL_DENIED"),
                "approval_denied").ConfigureAwait(false);
            await _finalizer.AbandonUnrecordedLeaseBestEffortAsync(lease, "approval_denied").ConfigureAwait(false);
            return !recorded && entry.Governance.EffectiveAuditMode == AgentToolAuditMode.Required
                ? GovernanceDenied("AGENT_TOOL_AUDIT_FAILURE")
                : GovernanceDenied("AGENT_TOOL_APPROVAL_DENIED");
        }

        var auditContext = CreateAuditContext(governance);
        var authorization = await _coordinator.ExecuteAsync(
            new AgentToolPreDispatchCoordinationRequest
            {
                Entry = entry,
                Lease = lease,
                Governance = governance,
                AuditContext = auditContext,
                Approval = approval
            },
            cancellationToken).ConfigureAwait(false);
        if (authorization.Kind == AgentToolPreDispatchAuthorizationKind.Terminal)
            return authorization.Outcome!;
        var reservation = authorization.Reservation!;
        var auditHandle = authorization.Receipt;

        if (renewal.HasFailed)
        {
            return await _finalizer.ReleaseAuditedBeforeDispatchAsync(
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
            return await _finalizer.FinishFenceIndeterminateAsync(
                auditHandle,
                auditContext,
                lease,
                reservation,
                "dispatch_fence_uncertain").ConfigureAwait(false);
        }
        if (!dispatchStarted)
        {
            return await _finalizer.ReleaseAuditedBeforeDispatchAsync(
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
            return await _finalizer.FinishIndeterminateAsync(
                auditHandle, auditContext, lease, reservation, "dispatcher_failure").ConfigureAwait(false);
        }

        if (renewal.HasFailed || capabilityResult.Status == CapabilityExecutionStatus.TimedOut)
        {
            return await _finalizer.FinishIndeterminateAsync(
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
                return await _finalizer.FinishIndeterminateAsync(
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
                return await _finalizer.FinishIndeterminateAsync(
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
                return await _finalizer.FinishIndeterminateAsync(
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
            settled = await _finalizer.FinalizeBudgetAsync(reservation, AgentToolBudgetReservationState.Committed, "dispatch_completed", auditContext.LogicalInvocationKey.TenantId)
                .ConfigureAwait(false);
        }
        catch
        {
            return await _finalizer.FinishIndeterminateWithoutBudgetAsync(
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
            return await _finalizer.FinishIndeterminateWithSettledBudgetAsync(
                auditHandle,
                auditContext,
                lease,
                settled,
                "invocation_completion_failure").ConfigureAwait(false);
        }

        if (auditHandle is not null)
        {
            var completedFinalization = AgentToolPreDispatchFinalizer.Finalization(
                auditHandle, auditContext, lease, settled, true,
                AgentToolGovernanceAttemptFinalState.Completed,
                AgentToolInvocationTerminalState.Completed,
                outcome,
                "dispatch_completed",
                auditFacts);
            var confirmation = await _finalizer.ConfirmAuditFinalizationAsync(
                completedFinalization,
                entry.Governance.EffectiveAuditMode).ConfigureAwait(false);

            if (confirmation == AgentToolAuditConfirmation.Indeterminate)
            {
                await _finalizer.MarkIndeterminateIgnoringFailureAsync(
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
            if (AgentToolPreDispatchFinalizer.MatchesPreparedCompletion(
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
            return AgentToolPreDispatchFinalizer.MatchesPreparedCompletion(
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
        var recorded = await _finalizer.RecordDecisionBestEffortAsync(
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
