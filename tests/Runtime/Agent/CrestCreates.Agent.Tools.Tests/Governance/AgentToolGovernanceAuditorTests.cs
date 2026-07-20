using System.Text.Json;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

public sealed class AgentToolGovernanceAuditorTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        16,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task RequiredPreDispatch_IsConcurrentlyIdempotentByAuditId()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(
            new GovernanceTestData.MutableTimeProvider(Now));
        var record = GovernanceTestData.PreDispatch(GovernanceTestData.Context());
        using var barrier = new Barrier(3);

        var firstTask = RecordAfterBarrierAsync();
        var secondTask = RecordAfterBarrierAsync();
        barrier.SignalAndWait();
        var handles = await Task.WhenAll(firstTask, secondTask);

        handles.Select(handle => handle.AuditId)
            .Distinct(StringComparer.Ordinal).Should().ContainSingle();
        handles.Should().OnlyContain(handle => handle.AcceptedAt == Now);

        async Task<AgentToolGovernanceAuditHandle> RecordAfterBarrierAsync()
        {
            return await Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await auditor.RecordPreDispatchAsync(record);
            });
        }
    }

    [Fact]
    public async Task RepeatedPreDispatch_WithChangedGovernanceConflicts()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var record = GovernanceTestData.PreDispatch(context);
        await auditor.RecordPreDispatchAsync(record);

        var changed = record with
        {
            Context = record.Context with
            {
                Governance = record.Context.Governance with
                {
                    EffectiveAuditMode = AgentToolAuditMode.BestEffort
                }
            }
        };

        var act = async () => await auditor.RecordPreDispatchAsync(changed);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RepeatedPreDispatch_WithChangedBudgetConflicts()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var record = GovernanceTestData.PreDispatch(context);
        await auditor.RecordPreDispatchAsync(record);

        var changed = record with
        {
            Context = record.Context with
            {
                Governance = record.Context.Governance with
                {
                    Budget = record.Context.Governance.Budget with { CostUnits = 99 }
                }
            },
            BudgetReservation = record.BudgetReservation with { CostUnits = 99 }
        };

        var act = async () => await auditor.RecordPreDispatchAsync(changed);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FirstFinalization_WithChangedAgentRolesHashIsRejected()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var preDispatch = GovernanceTestData.PreDispatch(context);
        var handle = await auditor.RecordPreDispatchAsync(preDispatch);
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed) with
        {
            Context = GovernanceTestData.AuditContext(context) with
            {
                AgentRolesHash = "changed-roles"
            }
        };

        var act = async () => await auditor.FinalizeAsync(finalization);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FirstFinalization_WithChangedLeaseAttemptIdIsRejected()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var preDispatch = GovernanceTestData.PreDispatch(context);
        var handle = await auditor.RecordPreDispatchAsync(preDispatch);
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed) with
        {
            Lease = preDispatch.Lease with { AttemptId = "different-attempt" }
        };

        var act = async () => await auditor.FinalizeAsync(finalization);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Finalization_IsIdempotentAndCannotBeChanged()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var preDispatch = GovernanceTestData.PreDispatch(context);
        var handle = await auditor.RecordPreDispatchAsync(preDispatch);
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed);

        await auditor.FinalizeAsync(finalization);
        await auditor.FinalizeAsync(finalization);
        var conflictingOutcome = finalization.Outcome with
        {
            Kind = AgentToolInvocationOutcomeKind.InternalContractFailure,
            Code = "agent_tool_output_contract_failure"
        };
        var conflicting = async () => await auditor.FinalizeAsync(finalization with
        {
            Outcome = conflictingOutcome,
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(conflictingOutcome),
            ReasonCode = "output_contract_failure"
        });

        await conflicting.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Finalization_ConflictsOnStructuredOutputIssuesAndContext()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        using var document = JsonDocument.Parse("{\"value\":1}");
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed) with
        {
            Outcome = Finalization(
                handle.AuditId,
                context,
                AgentToolBudgetReservationState.Committed,
                AgentToolGovernanceAttemptFinalState.Completed,
                AgentToolInvocationTerminalState.Completed).Outcome with
            {
                StructuredOutput = document.RootElement.Clone(),
                Issues = [new AgentToolInvocationIssue("safe_issue", "value")]
            }
        };
        finalization = finalization with
        {
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(finalization.Outcome)
        };
        await auditor.FinalizeAsync(finalization);

        var structuredConflict = async () => await auditor.FinalizeAsync(finalization with
        {
            Outcome = finalization.Outcome with
            {
                Code = "different-code"
            },
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(
                finalization.Outcome with
                {
                    Code = "different-code"
                })
        });
        var contextConflict = async () => await auditor.FinalizeAsync(finalization with
        {
            Context = finalization.Context with { AgentRolesHash = "different-roles" }
        });

        await structuredConflict.Should().ThrowAsync<InvalidOperationException>();
        await contextConflict.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FinalizationState_TransitionsFromNotFinalizedToFinalized()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed);

        var before = await auditor.GetFinalizationStateAsync(handle.AuditId);
        var persisted = await auditor.FinalizeAsync(finalization);
        var after = await auditor.GetFinalizationStateAsync(handle.AuditId);

        before.Status.Should().Be(AgentToolGovernanceFinalizationStatus.NotFinalized);
        before.Record.Should().BeNull();
        persisted.Status.Should().Be(AgentToolGovernanceFinalizationStatus.Finalized);
        persisted.Record.Should().Be(finalization);
        after.Should().Be(persisted);
    }

    [Fact]
    public async Task BudgetCommitted_InvocationIndeterminate_IsRepresentable()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Indeterminate,
            AgentToolInvocationTerminalState.Indeterminate);

        var act = async () => await auditor.FinalizeAsync(finalization);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnknownBudgetSettlement_InvocationIndeterminate_IsRepresentable()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Unknown,
            AgentToolGovernanceAttemptFinalState.Indeterminate,
            AgentToolInvocationTerminalState.Indeterminate);

        var act = async () => await auditor.FinalizeAsync(finalization);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeniedDecision_IsRecordedWithoutApprovalOrBudgetReservation()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.AuditContext(GovernanceTestData.Context());
        var record = new AgentToolGovernanceDecisionRecord
        {
            Context = context,
            Decision = AgentToolGovernanceDecisionState.Denied,
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                Code = "AGENT_TOOL_ROLE_DENIED",
                Message = "blocked"
            },
            ReasonCode = "role_denied"
        };

        await auditor.RecordDecisionAsync(record);

        auditor.Decisions.Should().ContainSingle().Which.ReasonCode.Should().Be("role_denied");
    }

    [Fact]
    public async Task DecisionConflict_WithSameAttemptIdCannotBeSilentlyIgnored()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.AuditContext(GovernanceTestData.Context());
        var record = new AgentToolGovernanceDecisionRecord
        {
            Context = context,
            Decision = AgentToolGovernanceDecisionState.Denied,
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                Code = "AGENT_TOOL_ROLE_DENIED",
                Message = "blocked"
            },
            ReasonCode = "role_denied"
        };
        await auditor.RecordDecisionAsync(record);

        var act = async () => await auditor.RecordDecisionAsync(record with
        {
            Decision = AgentToolGovernanceDecisionState.Indeterminate,
            Outcome = record.Outcome with
            {
                Kind = AgentToolInvocationOutcomeKind.InvocationIndeterminate,
                Code = "AGENT_TOOL_INVOCATION_INDETERMINATE"
            },
            ReasonCode = "budget_failure"
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UnknownAuditMode_FailsClosedBeforeAuditIdIsIssued()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context(auditMode: AgentToolAuditMode.Unknown);
        var record = GovernanceTestData.PreDispatch(context);

        var act = async () => await auditor.RecordPreDispatchAsync(record);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Finalization_MustMatchPreDispatchIdentity()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var mismatchedContext = GovernanceTestData.Context(
            attemptId: context.AttemptId,
            fingerprint: "different-fingerprint");
        var finalization = Finalization(
            handle.AuditId,
            mismatchedContext,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed);

        var act = async () => await auditor.FinalizeAsync(finalization);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DispatchFencingLoss_FinalizesReleasedAttemptWithoutLogicalTerminal()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Released,
            AgentToolGovernanceAttemptFinalState.Released,
            invocationState: null,
            dispatchStarted: false,
            outcomeKind: AgentToolInvocationOutcomeKind.InProgress);

        var act = async () => await auditor.FinalizeAsync(finalization);

        await act.Should().NotThrowAsync();
        await auditor.FinalizeAsync(finalization);
    }

    [Fact]
    public async Task InvocationStateAndOutcome_MustBeConsistent()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var malformed = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed,
            outcomeKind: AgentToolInvocationOutcomeKind.InProgress);

        var act = async () => await auditor.FinalizeAsync(malformed);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Finalization_RejectsOutcomeHashThatDoesNotMatchOutcome()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed);
        var differentOutcome = finalization.Outcome with { Code = "different-code" };

        var act = async () => await auditor.FinalizeAsync(finalization with
        {
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(differentOutcome)
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Finalization_RejectsChangedOutcomeThatReusesOriginalHash()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var context = GovernanceTestData.Context();
        var handle = await auditor.RecordPreDispatchAsync(
            GovernanceTestData.PreDispatch(context));
        var finalization = Finalization(
            handle.AuditId,
            context,
            AgentToolBudgetReservationState.Committed,
            AgentToolGovernanceAttemptFinalState.Completed,
            AgentToolInvocationTerminalState.Completed);
        await auditor.FinalizeAsync(finalization);

        var act = async () => await auditor.FinalizeAsync(finalization with
        {
            Outcome = finalization.Outcome with { Code = "different-code" }
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static AgentToolGovernanceFinalizationRecord Finalization(
        string auditId,
        AgentToolGovernanceContext context,
        AgentToolBudgetReservationState budgetState,
        AgentToolGovernanceAttemptFinalState attemptState,
        AgentToolInvocationTerminalState? invocationState,
        bool dispatchStarted = true,
        AgentToolInvocationOutcomeKind? outcomeKind = null)
    {
        var outcome = new AgentToolInvocationOutcome
        {
            Kind = outcomeKind ?? (invocationState == AgentToolInvocationTerminalState.Completed
                ? AgentToolInvocationOutcomeKind.Succeeded
                : AgentToolInvocationOutcomeKind.InvocationIndeterminate),
            Code = invocationState == AgentToolInvocationTerminalState.Completed
                ? "agent_tool_succeeded"
                : "agent_tool_indeterminate",
            Message = "safe-message"
        };
        return new()
        {
            AuditId = auditId,
            Context = GovernanceTestData.AuditContext(context),
            Lease = GovernanceTestData.Lease(context.AttemptId),
            DispatchStarted = dispatchStarted,
            BudgetReservation = GovernanceTestData.Reservation(context, budgetState),
            AttemptState = attemptState,
            InvocationState = invocationState,
            Outcome = outcome,
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(outcome),
            ReasonCode = invocationState == AgentToolInvocationTerminalState.Completed
                ? "completed"
                : invocationState == AgentToolInvocationTerminalState.Indeterminate
                    ? "post_dispatch_audit_failure"
                    : "dispatch_fencing_lost"
        };
    }
}
