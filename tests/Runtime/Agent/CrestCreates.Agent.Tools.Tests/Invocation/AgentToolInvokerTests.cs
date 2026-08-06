using System.Text.Json;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Invocation;
/// <summary>
/// Shared pre-dispatch state tracker for test gate mocks.
/// </summary>
internal sealed class TestPreDispatchStateTracker
{
    private AgentToolInvocationPreDispatchState _state = AgentToolInvocationPreDispatchState.Unknown;
    private AgentToolInvocationPreDispatchIntentSnapshot? _intent;
    private string? _boundReservationId;
    private AgentToolGovernancePreDispatchReceipt? _acceptedReceipt;
    private AgentToolPreDispatchIdentity _identity;
    private readonly Func<bool> _dispatchSucceeds;

    public TestPreDispatchStateTracker(Func<bool>? dispatchSucceeds = null)
    {
        _dispatchSucceeds = dispatchSucceeds ?? (() => true);
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPreparePreDispatchIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        _state = AgentToolInvocationPreDispatchState.Pending;
        _intent = request.Intent;
        _identity = new AgentToolPreDispatchIdentity(
            new AgentToolLogicalInvocationKey(null, "test", "test", "test", lease.AttemptId),
            lease.AttemptId);
        return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
        {
            State = _state,
            Intent = _intent
        });
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_state != AgentToolInvocationPreDispatchState.Pending)
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult { State = _state });
        _state = AgentToolInvocationPreDispatchState.Ready;
        _boundReservationId = request.ReservationId;
        return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
        {
            State = _state,
            Intent = _intent,
            BoundReservationId = _boundReservationId
        });
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindPreDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_state != AgentToolInvocationPreDispatchState.Ready)
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult { State = _state });
        _state = AgentToolInvocationPreDispatchState.Accepted;
        _acceptedReceipt = request.Receipt;
        return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
        {
            State = _state,
            Intent = _intent,
            BoundReservationId = _boundReservationId,
            AcceptedReceipt = _acceptedReceipt
        });
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
        {
            State = _state,
            Intent = _intent,
            BoundReservationId = _boundReservationId,
            AcceptedReceipt = _acceptedReceipt
        });
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPublishDenialRequest request,
        CancellationToken cancellationToken = default)
    {
        _state = AgentToolInvocationPreDispatchState.Abandoned;
        return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
        {
            State = _state,
            AbandonedReceipt = new AgentToolInvocationAbandonedReceipt
            {
                Identity = _identity,
                Outcome = request.Outcome,
                ReasonCode = request.ReasonCode,
                AbandonedAt = DateTimeOffset.UtcNow
            }
        });
    }

    public ValueTask<bool> TryMarkDispatchStartedAsync(
        AgentToolInvocationLease lease,
        AgentToolGovernancePreDispatchReceipt receipt,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        if (_state != AgentToolInvocationPreDispatchState.Accepted)
            return ValueTask.FromResult(false);
        if (!_dispatchSucceeds())
            return ValueTask.FromResult(false);
        _state = AgentToolInvocationPreDispatchState.DispatchStarted;
        return ValueTask.FromResult(true);
    }
}


public sealed class AgentToolInvokerTests
{
    [Fact]
    public async Task Invoke_SucceedsAndCompletedReplayDoesNotRedispatchOrReserveAgain()
    {
        var harness = CreateHarness();

        var first = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));
        var replay = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        replay.Should().Be(first);
        harness.Dispatcher.CallCount.Should().Be(1);
        harness.Budget.ReserveCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_PropagatesTrustedAgentIdentityAndRuntimeReferences()
    {
        var harness = CreateHarness();
        harness.Execution.CurrentValue = harness.Execution.CurrentValue! with
        {
            CausationId = "agent-decision-1"
        };

        var result = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        result.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        var context = harness.Dispatcher.LastContext!;
        context.CausationId.Should().Be("agent-decision-1");
        context.AccountabilityActor!.Kind.Should().Be("agent");
        context.AccountabilityActor.Id.Should().Be("agent-1");
        context.AccountabilityActor.InitiatedBy.Should().BeEquivalentTo(new { Kind = "user", Id = "user-1" });
        context.AccountabilityRuntimeReferences.Should().Contain(reference =>
            reference.Kind == "agent-session" && reference.Id == "execution-1");
        context.AccountabilityRuntimeReferences.Should().Contain(reference =>
            reference.Kind == "agent-invocation" && reference.Id == "invocation-1");
    }

    [Fact]
    public async Task Invoke_ChangedCallOriginForSameLogicalInvocationConflictsBeforeDispatch()
    {
        var harness = CreateHarness(automaticAllowed: true);
        (await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName)))
            .Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        harness.Execution.CurrentValue = harness.Execution.CurrentValue! with
        {
            CallOrigin = AgentToolCallOrigin.AutomaticSelection
        };

        var conflict = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        conflict.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationConflict);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_RoleDeniedToolBehavesAsUnknownAndRecordsGovernanceDecision()
    {
        var harness = CreateHarness();
        harness.Execution.CurrentValue = harness.Execution.CurrentValue! with
        {
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "viewer" }
        };

        var outcome = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.UnknownTool);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.ReserveCount.Should().Be(0);
        harness.Auditor!.Decisions.Should().ContainSingle(decision =>
            decision.ReasonCode == "role_denied");
        var decision = harness.Auditor.Decisions.Single();
        decision.Context.ArgumentsEvaluated.Should().BeFalse();
        decision.Context.ArgumentsHash.Should().BeNull();
    }

    [Fact]
    public async Task Invoke_TimedOutCapabilityConsumesIndeterminateBudgetAndBlocksReplay()
    {
        var harness = CreateHarness(CapabilityExecutionResult.Timeout(TimeSpan.Zero));

        var first = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(1);
        harness.Budget.LastFinalState.Should().Be(AgentToolBudgetReservationState.Indeterminate);
    }

    [Fact]
    public async Task Invoke_UnconfirmedPreDispatchAuditFencesInvocationAndNeverDispatches()
    {
        var harness = CreateHarness(requiredAudit: true, audit: new ThrowingAuditor());

        var outcome = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.LastFinalState.Should().Be(AgentToolBudgetReservationState.Released);
        var retry = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
    }

    [Fact]
    public async Task Invoke_RequiredPreDispatchResponseLossRecoversAcceptedAudit()
    {
        var audit = new PreDispatchResponseLossAuditor();
        var harness = CreateHarness(requiredAudit: true, audit: audit);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        // P0-03: authoritative read recovers the committed receipt — no second write.
        audit.RecordCalls.Should().Be(1);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_BestEffortPreDispatchResponseLossFinalizesSameAudit()
    {
        var audit = new PreDispatchResponseLossAuditor();
        var harness = CreateHarness(audit: audit);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        // P0-03: authoritative read recovers the committed receipt — no second write.
        audit.RecordCalls.Should().Be(1);
        audit.Inner.Finalizations.Should().ContainSingle();
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_AuthoritativeMissingPerformsSingleBoundedRecordRetry()
    {
        // P0-03: only an authoritative Missing lookup permits one identical
        // Record retry. The write itself fails once (response lost before commit
        // became observable), the authoritative read says Missing, and the
        // single bounded retry succeeds.
        var audit = new MissingThenAcceptsAuditor();
        var harness = CreateHarness(requiredAudit: true, audit: audit);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        audit.RecordCalls.Should().Be(2);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_LookupThrowsFencesIndeterminateWithoutRecordRetry()
    {
        // P0-03: a lookup that did not complete (Unavailable) must NOT retry the
        // write — it stays fenced Indeterminate so no second fuzzy commit window
        // is created.
        var audit = new ThrowingLookupAuditor();
        var harness = CreateHarness(requiredAudit: true, audit: audit);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        audit.RecordCalls.Should().Be(1);
        harness.Dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_NonObjectArgumentsFailBeforeLogicalInvocationAcquisition()
    {
        var harness = CreateHarness();
        using var document = JsonDocument.Parse("[]");

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName, document.RootElement.Clone()));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvalidRequest);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.ReserveCount.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_InvalidSchemaTypesRemainInvalidRequestsAndDoNotEscape()
    {
        var harness = CreateHarness(withIntInputSchema: true);
        using var document = JsonDocument.Parse("{\"Value\":\"not-an-int\"}");
        using var secondDocument = JsonDocument.Parse("{\"Value\":{\"nested\":true}}");

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName, document.RootElement.Clone()));
        var secondOutcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName, secondDocument.RootElement.Clone()));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvalidRequest);
        secondOutcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvalidRequest);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.ReserveCount.Should().Be(0);
        harness.Auditor!.Decisions.Select(decision => decision.Context.ArgumentsHash)
            .Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("{\"value\":1}")]
    [InlineData("{\"value\":true}")]
    [InlineData("{\"value\":{\"nested\":1}}")]
    public async Task Invoke_NoInputToolRejectsNonStringArgumentsWithoutFingerprintException(string rawArguments)
    {
        var harness = CreateHarness();
        using var document = JsonDocument.Parse(rawArguments);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName, document.RootElement.Clone()));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvalidRequest);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.ReserveCount.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_UncertainDispatchFenceReleasesBudgetButMarksInvocationIndeterminate()
    {
        var gate = new ThrowingDispatchFenceGate();
        var harness = CreateHarness(invocationGate: gate);

        var outcome = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.LastFinalState.Should().Be(AgentToolBudgetReservationState.Released);
        gate.MarkIndeterminateCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_BudgetReservationExceptionMarksLogicalInvocationIndeterminate()
    {
        var harness = CreateHarness(throwOnBudgetReserve: true);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Auditor!.Decisions.Should().ContainSingle(decision =>
            decision.ReasonCode == "budget_reservation_uncertain");
    }

    [Fact]
    public async Task Invoke_BudgetSettlementExceptionFinalizesAuditAsUnknownAndIndeterminate()
    {
        var harness = CreateHarness(throwOnBudgetFinalize: true);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Auditor!.Finalizations.Should().ContainSingle().Which.Should().Match<AgentToolGovernanceFinalizationRecord>(
            finalization => finalization.BudgetReservation.State == AgentToolBudgetReservationState.Unknown
                && finalization.InvocationState == AgentToolInvocationTerminalState.Indeterminate);
    }

    [Fact]
    public async Task Invoke_CompletionResponseLossFinalizesIndeterminateAuditAndDoesNotReplay()
    {
        var gate = new ThrowingCompletionGate();
        var harness = CreateHarness(invocationGate: gate);

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Auditor!.Finalizations.Should().ContainSingle().Which.Should().Match<AgentToolGovernanceFinalizationRecord>(
            finalization => finalization.AttemptState == AgentToolGovernanceAttemptFinalState.Indeterminate
                && finalization.InvocationState == AgentToolInvocationTerminalState.Indeterminate
                && finalization.ReasonCode == "invocation_completion_failure");
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_PublishResponseLossQueriesDurableStateAndReplaysCompletedOutcome()
    {
        var harness = CreateHarness(invocationGate: new PublishResponseLossGate());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var replay = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        replay.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_MismatchedPublishAssociationQueriesDurableCompletion()
    {
        var harness = CreateHarness(invocationGate: new MismatchedPublishResultGate());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_DeniedBudgetWithReservationIsIndeterminateAndCannotRetry()
    {
        var harness = CreateHarness(malformedDeniedBudget: true);

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Auditor!.Decisions.Should().ContainSingle().Which.ObservedReservation.Should().NotBeNull();
    }

    [Fact]
    public async Task Invoke_RequiredDecisionAuditFailureReturnsStableAuditFailure()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new ThrowingAuditor(throwDecision: true));
        harness.Execution.CurrentValue = harness.Execution.CurrentValue! with
        {
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "viewer" }
        };

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.UnknownTool);
        outcome.Code.Should().Be("AGENT_TOOL_UNKNOWN");
        harness.Dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_UnconfirmedRequiredAuditLeavesCompletionPending()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new FinalizationThrowingAuditor());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InProgress);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_RequiredAuditResponseLossQueriesFinalizationAndPublishesCompletion()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new AuditResponseLossAuditor());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var replay = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        replay.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_MismatchedAuditFinalizationQueriesDurableState()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new MismatchedFinalizationAuditor());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var replay = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        replay.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_BestEffortConfirmedIndeterminateAuditBlocksCompletion()
    {
        var harness = CreateHarness(audit: new IndeterminateFinalizationAuditor());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_AuditConflictLeavesCompletionPendingForReconciliation()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new ConflictingFinalizationAuditor());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InProgress);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_PropagatesExactInvocationBindingAndFactBuffer()
    {
        var harness = CreateHarness();

        _ = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        harness.Dispatcher.LastContext.Should().NotBeNull();
        harness.Dispatcher.LastContext!.Items[AgentCapabilityContextItemNames.InvocationBindingSnapshot]
            .Should().BeOfType<AgentToolInvocationBindingSnapshot>();
        harness.Dispatcher.LastContext.Items[AgentCapabilityContextItemNames.InvocationFactBuffer]
            .Should().BeAssignableTo<IAgentToolInvocationFactSink>();
    }

    [Fact]
    public async Task Invoke_ReleasedIndeterminateAuditFencesLogicalInvocation()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new IndeterminateFinalizationAuditor(),
            invocationGate: new RejectingDispatchFenceGate());

        var first = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_RequiredReleasedFinalizationNotFinalizedDoesNotCountAsSuccess()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new FinalizationThrowingAuditor(),
            invocationGate: new RejectingDispatchFenceGate());

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.GovernanceDenied);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InProgress);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.LastFinalState.Should().Be(AgentToolBudgetReservationState.Released);
    }

    [Fact]
    public async Task Invoke_BlockingReleasedAuditKeepsConcurrentAcquireInProgress()
    {
        var audit = new BlockingFinalizationAuditor();
        var harness = CreateHarness(
            requiredAudit: true,
            audit: audit,
            invocationGate: new RejectingDispatchFenceGate());
        var firstTask = harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName)).AsTask();

        await audit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var concurrent = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        concurrent.Kind.Should().Be(AgentToolInvocationOutcomeKind.InProgress);
        audit.Release.TrySetResult();
        var first = await firstTask;
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.GovernanceDenied);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InProgress);
        harness.Dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_RequiredIndeterminateFinalizationNotFinalizedDoesNotThrow()
    {
        var harness = CreateHarness(
            requiredAudit: true,
            audit: new FinalizationThrowingAuditor(),
            throwOnBudgetFinalize: true);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_CompletionPendingIsNotVisibleAsCompletedReplay()
    {
        var audit = new BlockingFinalizationAuditor();
        var harness = CreateHarness(requiredAudit: true, audit: audit);

        var firstTask = harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName)).AsTask();
        await audit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var concurrent = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        concurrent.Kind.Should().Be(AgentToolInvocationOutcomeKind.InProgress);
        audit.Release.TrySetResult();
        var first = await firstTask;
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InProgress);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    private static Harness CreateHarness(
        CapabilityExecutionResult? dispatcherResult = null,
        bool automaticAllowed = false,
        bool requiredAudit = false,
        IAgentToolGovernanceAuditor? audit = null,
        IAgentToolInvocationGate? invocationGate = null,
        bool throwOnBudgetReserve = false,
        bool throwOnBudgetFinalize = false,
        bool malformedDeniedBudget = false,
        bool withIntInputSchema = false)
    {
        var inputSchema = withIntInputSchema
            ? AgentToolRuntimeTestFixture.Schema("invoker-input-schema")
            : null;
        var capability = AgentToolRuntimeTestFixture.Capability(
            "invoker-capability",
            input: inputSchema is null
                ? null
                : new VersionedDescriptorRef<SchemaDescriptor>(inputSchema.Id, inputSchema.Version));
        var source = AgentToolRuntimeTestFixture.Tool(
            $"invoker-tool-{Guid.NewGuid():N}",
            capability.Id,
            $"invoker.tool.{Guid.NewGuid():N}",
            audit: requiredAudit ? AgentToolAuditMode.Required : AgentToolAuditMode.BestEffort);
        var tool = automaticAllowed ? CopyWithAutomaticSelection(source) : source;
        if (withIntInputSchema)
            AgentToolRuntimeTestFixture.RegisterInputDtoBinding(tool);
        else
            AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        var snapshot = AgentToolRuntimeTestFixture.SnapshotBuilder(
            AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
            AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
            inputSchema is null
                ? AgentToolRuntimeTestFixture.BuildSchemaRegistry()
                : AgentToolRuntimeTestFixture.BuildSchemaRegistry(inputSchema))
            .Build();
        var snapshots = new AgentToolRuntimeSnapshotProvider();
        snapshots.Publish(snapshot);

        var execution = new MutableExecutionContextAccessor
        {
            CurrentValue = new AgentExecutionContext
            {
                ExecutionId = "execution-1",
                InvocationId = "invocation-1",
                AgentId = "agent-1",
                AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
                CallOrigin = AgentToolCallOrigin.ExplicitRequest
            }
        };
        var dispatcher = new RecordingDispatcher(
            dispatcherResult ?? CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var budget = new RecordingBudgetGate(
            throwOnBudgetReserve,
            throwOnBudgetFinalize,
            malformedDeniedBudget);
        var inMemoryAudit = audit is null
            ? new DevelopmentInMemoryAgentToolGovernanceAuditor()
            : null;
        var gate = invocationGate ?? new DevelopmentInMemoryAgentToolInvocationGate();
        var abandoner = gate as IAgentToolInvocationLeaseAbandoner
            ?? throw new InvalidOperationException("The test gate must provide lease abandonment.");
        var invoker = new AgentToolInvoker(
            snapshots,
            execution,
            new TestCurrentUser(),
            new TestTenantContext(),
            gate,
            abandoner,
            new FailClosedAgentToolApprovalGate(),
            budget,
            audit ?? inMemoryAudit!,
            dispatcher,
            new SchemaValidator(),
            new AgentToolInvocationFingerprintBuilder(),
            new AgentCapabilityIdempotencyKeyBuilder(),
            new AgentToolResultMapper());
        return new Harness(tool.ToolName, invoker, execution, dispatcher, budget, inMemoryAudit);
    }

    private static AgentToolGovernanceFinalizationResult Finalized(
        AgentToolGovernanceFinalizationRecord record)
        => new()
        {
            Status = AgentToolGovernanceFinalizationStatus.Finalized,
            Record = record
        };

    private static AgentCapabilityToolDescriptor CopyWithAutomaticSelection(
        AgentCapabilityToolDescriptor source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            Version = source.Version,
            State = source.State,
            Capability = source.Capability,
            ToolName = source.ToolName,
            Title = source.Title,
            Description = source.Description,
            SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed,
            SideEffectKind = source.SideEffectKind,
            RiskFloor = source.RiskFloor,
            ApprovalMode = source.ApprovalMode,
            Budget = source.Budget,
            AuditMode = source.AuditMode,
            AllowedAgentRoles = source.AllowedAgentRoles
        };

    private sealed record Harness(
        string ToolName,
        AgentToolInvoker Invoker,
        MutableExecutionContextAccessor Execution,
        RecordingDispatcher Dispatcher,
        RecordingBudgetGate Budget,
        DevelopmentInMemoryAgentToolGovernanceAuditor? Auditor);

    private sealed class MutableExecutionContextAccessor : IAgentExecutionContextAccessor
    {
        public AgentExecutionContext? CurrentValue { get; set; }
        public AgentExecutionContext? Current => CurrentValue;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId => "tenant-1";
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string Id => "user-1";
        public string UserName => "user";
        public bool IsAuthenticated => true;
        public string TenantId => "tenant-1";
        public string[] Roles => [];
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }

    private sealed class RecordingDispatcher(CapabilityExecutionResult result) : ICapabilityDispatcher
    {
        public int CallCount { get; private set; }
        public CapabilityExecutionContext? LastContext { get; private set; }

        public Task<CapabilityExecutionResult> DispatchAsync(
            CapabilityDescriptor descriptor,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
        {
            CallCount++;
            var context = new CapabilityExecutionContext
            {
                ServiceProvider = EmptyServiceProvider.Instance,
                UserId = "user-1"
            };
            LastContext = context;
            configureContext?.Invoke(context);
            return Task.FromResult(result);
        }

        public Task<CapabilityExecutionResult> DispatchAsync(
            string capabilityId,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingBudgetGate(
        bool throwOnReserve,
        bool throwOnFinalize,
        bool malformedDenied) : IAgentToolBudgetGate
    {
        private AgentToolBudgetReservation? _reservation;

        public int ReserveCount { get; private set; }
        public AgentToolBudgetReservationState? LastFinalState { get; private set; }

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
            AgentToolBudgetReserveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (throwOnReserve)
                throw new InvalidOperationException("budget response unavailable");
            ReserveCount++;
            _reservation = new AgentToolBudgetReservation
            {
                ReservationId = $"reservation-{ReserveCount}",
                AttemptId = request.Context.AttemptId,
                InvocationFingerprint = request.Context.InvocationFingerprint,
                Category = request.Context.Governance.Budget.Category,
                CostUnits = request.Context.Governance.Budget.CostUnits,
                MaxCallsPerExecution = request.Context.Governance.Budget.MaxCallsPerExecution,
                State = AgentToolBudgetReservationState.Reserved
            };
            if (malformedDenied)
            {
                return ValueTask.FromResult(new AgentToolBudgetReserveResult
                {
                    Status = AgentToolBudgetReserveStatus.Denied,
                    Reservation = _reservation,
                    ReasonCode = "budget_denied"
                });
            }
            return ValueTask.FromResult(new AgentToolBudgetReserveResult
            {
                Status = AgentToolBudgetReserveStatus.Reserved,
                Reservation = _reservation
            });
        }

        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(
            AgentToolBudgetFinalizeRequest request,
            CancellationToken cancellationToken = default)
        {
            if (throwOnFinalize)
                throw new InvalidOperationException("budget settlement unavailable");
            LastFinalState = request.RequestedState;
            _reservation = _reservation! with { State = request.RequestedState };
            return ValueTask.FromResult(_reservation);
        }
    public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentToolBudgetReservationReadResult { Status = AgentToolBudgetReadStatus.Reserved });

    }

    private sealed class ThrowingAuditor(bool throwDecision = false) : IAgentToolGovernanceAuditor
    {
        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => throwDecision
                ? ValueTask.FromException(new InvalidOperationException("decision audit unavailable"))
                : ValueTask.CompletedTask;

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<AgentToolGovernancePreDispatchWriteResult>(new InvalidOperationException("audit unavailable"));

        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Finalized(record));

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.Unknown
            });
    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult { Status = AgentToolGovernancePreDispatchReadStatus.Accepted });

    }

    private sealed class ThrowingDispatchFenceGate : IAgentToolInvocationGate, IAgentToolInvocationLeaseAbandoner
    {
        private readonly DevelopmentInMemoryAgentToolInvocationGate _inner = new();

        public int MarkIndeterminateCount { get; private set; }

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
            AgentToolInvocationAcquireRequest request,
            CancellationToken cancellationToken = default)
            => _inner.AcquireAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationLease> RenewAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.RenewAsync(lease, cancellationToken);

        public ValueTask<bool> TryMarkDispatchStartedAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken cancellationToken = default)
            => _preDispatchTracker.TryMarkDispatchStartedAsync(lease, receipt, reservationId, cancellationToken);

        public ValueTask PrepareCompletionAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareCompletionRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareCompletionAsync(lease, request, cancellationToken);

        public ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishCompletionAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetCompletionStateAsync(lease, cancellationToken);

        public ValueTask PrepareReleaseAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareReleaseRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareReleaseAsync(lease, request, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishReleaseAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetReleaseStateAsync(lease, cancellationToken);

        public async ValueTask MarkIndeterminateAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
        {
            MarkIndeterminateCount++;
            await _inner.MarkIndeterminateAsync(lease, reasonCode, cancellationToken);
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.ReleaseByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.AbandonByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request,
            CancellationToken cancellationToken = default)
            => _inner.TryBeginPreDispatchReconciliationAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.CompletePreDispatchReconciliationAsync(claim, kind, reasonCode, cancellationToken);


        public ValueTask AbandonUnrecordedLeaseAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.AbandonUnrecordedLeaseAsync(lease, reasonCode, cancellationToken);
    private readonly TestPreDispatchStateTracker _preDispatchTracker = new TestPreDispatchStateTracker(() => throw new InvalidOperationException("fence result unavailable"));

    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPreparePreDispatchIntentRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.PreparePreDispatchIntentAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindReservationRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.BindPreDispatchReservationAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindPreDispatchRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.BindAcceptedPreDispatchAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.GetPreDispatchStateAsync(identity, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPublishDenialRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.PublishBudgetDenialAsync(lease, request, cancellationToken);


    }

    private sealed class RejectingDispatchFenceGate : IAgentToolInvocationGate, IAgentToolInvocationLeaseAbandoner
    {
        private readonly DevelopmentInMemoryAgentToolInvocationGate _inner = new();

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
            AgentToolInvocationAcquireRequest request,
            CancellationToken cancellationToken = default)
            => _inner.AcquireAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationLease> RenewAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.RenewAsync(lease, cancellationToken);

        public ValueTask<bool> TryMarkDispatchStartedAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken cancellationToken = default)
            => _preDispatchTracker.TryMarkDispatchStartedAsync(lease, receipt, reservationId, cancellationToken);

        public ValueTask PrepareCompletionAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareCompletionRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareCompletionAsync(lease, request, cancellationToken);

        public ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishCompletionAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetCompletionStateAsync(lease, cancellationToken);

        public ValueTask PrepareReleaseAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareReleaseRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareReleaseAsync(lease, request, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishReleaseAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetReleaseStateAsync(lease, cancellationToken);

        public ValueTask MarkIndeterminateAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.MarkIndeterminateAsync(lease, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.ReleaseByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.AbandonByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request,
            CancellationToken cancellationToken = default)
            => _inner.TryBeginPreDispatchReconciliationAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.CompletePreDispatchReconciliationAsync(claim, kind, reasonCode, cancellationToken);


        public ValueTask AbandonUnrecordedLeaseAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.AbandonUnrecordedLeaseAsync(lease, reasonCode, cancellationToken);
    private readonly TestPreDispatchStateTracker _preDispatchTracker = new TestPreDispatchStateTracker(() => false);

    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPreparePreDispatchIntentRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.PreparePreDispatchIntentAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindReservationRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.BindPreDispatchReservationAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindPreDispatchRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.BindAcceptedPreDispatchAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.GetPreDispatchStateAsync(identity, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPublishDenialRequest request,
        CancellationToken cancellationToken = default)
        => _preDispatchTracker.PublishBudgetDenialAsync(lease, request, cancellationToken);


    }

    private sealed class FinalizationThrowingAuditor : IAgentToolGovernanceAuditor
    {
        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernancePreDispatchWriteResult
            {
                Status = AgentToolGovernancePreDispatchWriteStatus.Accepted,
                Receipt = new AgentToolGovernancePreDispatchReceipt
                {
                    Identity = new AgentToolPreDispatchIdentity(
                        record.Context.LogicalInvocationKey,
                        record.Lease.AttemptId),
                    AuditId = "audit-test",
                    AcceptedAt = DateTimeOffset.UtcNow
                }
            });

    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult { Status = AgentToolGovernancePreDispatchReadStatus.Accepted });

        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<AgentToolGovernanceFinalizationResult>(
                new InvalidOperationException("finalization unavailable"));

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.NotFinalized
            });
    }

    private sealed class AuditResponseLossAuditor : IAgentToolGovernanceAuditor
    {
        private readonly DevelopmentInMemoryAgentToolGovernanceAuditor _inner = new();

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordDecisionAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordPreDispatchAsync(record, cancellationToken);

        public async ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
        {
            _ = await _inner.FinalizeAsync(record, cancellationToken);
            throw new IOException("audit finalization response lost");
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => _inner.GetFinalizationStateAsync(auditId, tenantId, cancellationToken);
    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => _inner.GetPreDispatchStateAsync(identity, cancellationToken);

    }

    private sealed class PreDispatchResponseLossAuditor : IAgentToolGovernanceAuditor
    {
        public DevelopmentInMemoryAgentToolGovernanceAuditor Inner { get; } = new();

        public int RecordCalls { get; private set; }

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => Inner.RecordDecisionAsync(record, cancellationToken);

        public async ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
        {
            RecordCalls++;
            var handle = await Inner.RecordPreDispatchAsync(record, cancellationToken);
            if (RecordCalls == 1)
                throw new IOException("pre-dispatch audit response lost");
            return handle;
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
            => Inner.FinalizeAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => Inner.GetFinalizationStateAsync(auditId, tenantId, cancellationToken);
    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => Inner.GetPreDispatchStateAsync(identity, cancellationToken);

    }

    private sealed class MissingThenAcceptsAuditor : IAgentToolGovernanceAuditor
    {
        private readonly DevelopmentInMemoryAgentToolGovernanceAuditor _inner = new();

        public int RecordCalls { get; private set; }

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordDecisionAsync(record, cancellationToken);

        public async ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
        {
            RecordCalls++;
            if (RecordCalls == 1)
                throw new IOException("pre-dispatch audit write failed before commit observable");
            return await _inner.RecordPreDispatchAsync(record, cancellationToken);
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
            => _inner.FinalizeAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => _inner.GetFinalizationStateAsync(auditId, tenantId, cancellationToken);

        // Authoritative Missing: the read completed and proves nothing was persisted.
        public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(
            AgentToolPreDispatchIdentity identity,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
            {
                Status = AgentToolGovernancePreDispatchReadStatus.Missing
            });
    }

    private sealed class ThrowingLookupAuditor : IAgentToolGovernanceAuditor
    {
        private readonly DevelopmentInMemoryAgentToolGovernanceAuditor _inner = new();

        public int RecordCalls { get; private set; }

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordDecisionAsync(record, cancellationToken);

        public async ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
        {
            RecordCalls++;
            if (RecordCalls == 1)
                throw new IOException("pre-dispatch audit response lost after commit");
            return await _inner.RecordPreDispatchAsync(record, cancellationToken);
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
            => _inner.FinalizeAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => _inner.GetFinalizationStateAsync(auditId, tenantId, cancellationToken);

        // Lookup did not complete — indistinguishable from a partial commit.
        public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(
            AgentToolPreDispatchIdentity identity,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<AgentToolGovernancePreDispatchReadResult>(
                new IOException("pre-dispatch lookup unavailable"));
    }

    private sealed class MismatchedFinalizationAuditor : IAgentToolGovernanceAuditor
    {
        private readonly DevelopmentInMemoryAgentToolGovernanceAuditor _inner = new();

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordDecisionAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordPreDispatchAsync(record, cancellationToken);

        public async ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
        {
            _ = await _inner.FinalizeAsync(record, cancellationToken);
            var stale = record with
            {
                Outcome = record.Outcome with { Message = "stale response" }
            };
            stale = stale with
            {
                OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(stale.Outcome)
            };
            return Finalized(stale);
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => _inner.GetFinalizationStateAsync(auditId, tenantId, cancellationToken);
    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => _inner.GetPreDispatchStateAsync(identity, cancellationToken);

    }

    private sealed class IndeterminateFinalizationAuditor : IAgentToolGovernanceAuditor
    {
        private readonly DevelopmentInMemoryAgentToolGovernanceAuditor _inner = new();

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordDecisionAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordPreDispatchAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
        {
            if (record.AttemptState is AgentToolGovernanceAttemptFinalState.Completed
                or AgentToolGovernanceAttemptFinalState.Released)
            {
                var indeterminate = record with
                {
                    AttemptState = AgentToolGovernanceAttemptFinalState.Indeterminate,
                    InvocationState = AgentToolInvocationTerminalState.Indeterminate,
                    Outcome = record.Outcome with
                    {
                        Kind = AgentToolInvocationOutcomeKind.InvocationIndeterminate,
                        Code = "AGENT_TOOL_INVOCATION_INDETERMINATE"
                    },
                    ReasonCode = "audit_indeterminate"
                };
                indeterminate = indeterminate with
                {
                    OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(indeterminate.Outcome)
                };
                _ = _inner.FinalizeAsync(indeterminate, cancellationToken);
                return ValueTask.FromResult(Finalized(indeterminate));
            }

            return _inner.FinalizeAsync(record, cancellationToken);
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => _inner.GetFinalizationStateAsync(auditId, tenantId, cancellationToken);
    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => _inner.GetPreDispatchStateAsync(identity, cancellationToken);

    }

    private sealed class ConflictingFinalizationAuditor : IAgentToolGovernanceAuditor
    {
        private readonly DevelopmentInMemoryAgentToolGovernanceAuditor _inner = new();

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordDecisionAsync(record, cancellationToken);

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => _inner.RecordPreDispatchAsync(record, cancellationToken);

        public async ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
        {
            var conflicting = record with
            {
                Outcome = record.Outcome with { Message = "different terminal content" }
            };
            conflicting = conflicting with
            {
                OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(conflicting.Outcome)
            };
            return await _inner.FinalizeAsync(conflicting, cancellationToken);
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => _inner.GetFinalizationStateAsync(auditId, tenantId, cancellationToken);
    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => _inner.GetPreDispatchStateAsync(identity, cancellationToken);

    }

    private sealed class BlockingFinalizationAuditor : IAgentToolGovernanceAuditor
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernancePreDispatchWriteResult
            {
                Status = AgentToolGovernancePreDispatchWriteStatus.Accepted,
                Receipt = new AgentToolGovernancePreDispatchReceipt
                {
                    Identity = new AgentToolPreDispatchIdentity(
                        record.Context.LogicalInvocationKey,
                        record.Lease.AttemptId),
                    AuditId = "audit-blocking",
                    AcceptedAt = DateTimeOffset.UtcNow
                }
            });

    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult { Status = AgentToolGovernancePreDispatchReadStatus.Accepted });

        public async ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException("finalization unavailable");
        }

        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.NotFinalized
            });
    }

    private sealed class ThrowingCompletionGate : IAgentToolInvocationGate, IAgentToolInvocationLeaseAbandoner
    {
        private readonly DevelopmentInMemoryAgentToolInvocationGate _inner = new();

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
            AgentToolInvocationAcquireRequest request,
            CancellationToken cancellationToken = default)
            => _inner.AcquireAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationLease> RenewAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.RenewAsync(lease, cancellationToken);

        public ValueTask<bool> TryMarkDispatchStartedAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken cancellationToken = default)
            => _inner.TryMarkDispatchStartedAsync(lease, receipt, reservationId, cancellationToken);

        public ValueTask PrepareCompletionAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareCompletionRequest request,
            CancellationToken cancellationToken = default)
            => PrepareThenLoseResponseAsync(lease, request, cancellationToken);

        private async ValueTask PrepareThenLoseResponseAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareCompletionRequest request,
            CancellationToken cancellationToken)
        {
            await _inner.PrepareCompletionAsync(lease, request, cancellationToken);
            throw new IOException("completion response lost");
        }

        public ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishCompletionAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetCompletionStateAsync(lease, cancellationToken);

        public ValueTask PrepareReleaseAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareReleaseRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareReleaseAsync(lease, request, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishReleaseAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetReleaseStateAsync(lease, cancellationToken);

        public ValueTask MarkIndeterminateAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.MarkIndeterminateAsync(lease, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.ReleaseByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.AbandonByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request,
            CancellationToken cancellationToken = default)
            => _inner.TryBeginPreDispatchReconciliationAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.CompletePreDispatchReconciliationAsync(claim, kind, reasonCode, cancellationToken);


        public ValueTask AbandonUnrecordedLeaseAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.AbandonUnrecordedLeaseAsync(lease, reasonCode, cancellationToken);
    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPreparePreDispatchIntentRequest request,
        CancellationToken cancellationToken = default)
        => _inner.PreparePreDispatchIntentAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindReservationRequest request,
        CancellationToken cancellationToken = default)
        => _inner.BindPreDispatchReservationAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindPreDispatchRequest request,
        CancellationToken cancellationToken = default)
        => _inner.BindAcceptedPreDispatchAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
        => _inner.GetPreDispatchStateAsync(identity, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPublishDenialRequest request,
        CancellationToken cancellationToken = default)
        => _inner.PublishBudgetDenialAsync(lease, request, cancellationToken);


    }

    private sealed class PublishResponseLossGate : IAgentToolInvocationGate, IAgentToolInvocationLeaseAbandoner
    {
        private readonly DevelopmentInMemoryAgentToolInvocationGate _inner = new();

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
            AgentToolInvocationAcquireRequest request,
            CancellationToken cancellationToken = default)
            => _inner.AcquireAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationLease> RenewAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.RenewAsync(lease, cancellationToken);

        public ValueTask<bool> TryMarkDispatchStartedAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken cancellationToken = default)
            => _inner.TryMarkDispatchStartedAsync(lease, receipt, reservationId, cancellationToken);

        public ValueTask PrepareCompletionAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareCompletionRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareCompletionAsync(lease, request, cancellationToken);

        public async ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
        {
            _ = await _inner.PublishCompletionAsync(lease, cancellationToken);
            throw new IOException("publish response lost");
        }

        public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetCompletionStateAsync(lease, cancellationToken);

        public ValueTask PrepareReleaseAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareReleaseRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareReleaseAsync(lease, request, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishReleaseAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetReleaseStateAsync(lease, cancellationToken);

        public ValueTask MarkIndeterminateAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.MarkIndeterminateAsync(lease, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.ReleaseByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.AbandonByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request,
            CancellationToken cancellationToken = default)
            => _inner.TryBeginPreDispatchReconciliationAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.CompletePreDispatchReconciliationAsync(claim, kind, reasonCode, cancellationToken);


        public ValueTask AbandonUnrecordedLeaseAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.AbandonUnrecordedLeaseAsync(lease, reasonCode, cancellationToken);
    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPreparePreDispatchIntentRequest request,
        CancellationToken cancellationToken = default)
        => _inner.PreparePreDispatchIntentAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindReservationRequest request,
        CancellationToken cancellationToken = default)
        => _inner.BindPreDispatchReservationAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindPreDispatchRequest request,
        CancellationToken cancellationToken = default)
        => _inner.BindAcceptedPreDispatchAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
        => _inner.GetPreDispatchStateAsync(identity, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPublishDenialRequest request,
        CancellationToken cancellationToken = default)
        => _inner.PublishBudgetDenialAsync(lease, request, cancellationToken);


    }

    private sealed class MismatchedPublishResultGate : IAgentToolInvocationGate, IAgentToolInvocationLeaseAbandoner
    {
        private readonly DevelopmentInMemoryAgentToolInvocationGate _inner = new();
        private AgentToolInvocationOutcome? _outcome;

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
            AgentToolInvocationAcquireRequest request,
            CancellationToken cancellationToken = default)
            => _inner.AcquireAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationLease> RenewAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.RenewAsync(lease, cancellationToken);

        public ValueTask<bool> TryMarkDispatchStartedAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken cancellationToken = default)
            => _inner.TryMarkDispatchStartedAsync(lease, receipt, reservationId, cancellationToken);

        public ValueTask PrepareCompletionAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            _outcome = request.Outcome;
            return _inner.PrepareCompletionAsync(lease, request, cancellationToken);
        }

        public async ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
        {
            var published = await _inner.PublishCompletionAsync(lease, cancellationToken);
            return published with
            {
                AuditId = "wrong-audit",
                BudgetReservationId = "wrong-reservation"
            };
        }

        public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetCompletionStateAsync(lease, cancellationToken);

        public ValueTask PrepareReleaseAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareReleaseRequest request,
            CancellationToken cancellationToken = default)
            => _inner.PrepareReleaseAsync(lease, request, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.PublishReleaseAsync(lease, cancellationToken);

        public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.GetReleaseStateAsync(lease, cancellationToken);

        public ValueTask MarkIndeterminateAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.MarkIndeterminateAsync(lease, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.ReleaseByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => _inner.AbandonByIdentityAsync(identity, reasonCode, cancellationToken);

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request,
            CancellationToken cancellationToken = default)
            => _inner.TryBeginPreDispatchReconciliationAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.CompletePreDispatchReconciliationAsync(claim, kind, reasonCode, cancellationToken);


        public ValueTask AbandonUnrecordedLeaseAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => _inner.AbandonUnrecordedLeaseAsync(lease, reasonCode, cancellationToken);
    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPreparePreDispatchIntentRequest request,
        CancellationToken cancellationToken = default)
        => _inner.PreparePreDispatchIntentAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindReservationRequest request,
        CancellationToken cancellationToken = default)
        => _inner.BindPreDispatchReservationAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindPreDispatchRequest request,
        CancellationToken cancellationToken = default)
        => _inner.BindAcceptedPreDispatchAsync(lease, request, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
        => _inner.GetPreDispatchStateAsync(identity, cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPublishDenialRequest request,
        CancellationToken cancellationToken = default)
        => _inner.PublishBudgetDenialAsync(lease, request, cancellationToken);


    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }
}
