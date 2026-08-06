using CrestCreates.Agent.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Pure policy tests. The policy is the single place that decides recovery
/// composition; every authority combination maps to exactly one decision with
/// one ReasonCode, and terminal decisions are claim-first. A table-driven
/// matrix covers every composed row plus the defensive post-dispatch gate
/// guard.
///
/// The internal policy action enums are passed through the theory as their
/// int backing values so the test API stays public (xUnit discovers public
/// test methods only) while the enums themselves remain internal.
/// </summary>
public class AgentToolPreDispatchRecoveryPolicyTests
{
    private static readonly AgentToolPreDispatchRecoveryPolicy Policy = new();

    public static TheoryData<AgentToolInvocationPreDispatchState, AgentToolBudgetReadStatus, AgentToolGovernancePreDispatchReadStatus,
        AgentToolPreDispatchReconciliationStatus, int, int, int, string> DecisionMatrix => new()
    {
        // ── Pending gate ────────────────────────────────────────────────────────────
        // §7.7: Pending + Missing + Missing → Abandoned.
        {
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Missing,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndAbandon,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "abandoned_unrecorded"
        },
        // CW04/CW05: Pending + Reserved + Missing → release budget, abandon.
        {
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndAbandon,
            (int)AgentToolPreDispatchBudgetAction.FinalizeReleased,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_reserved_no_checkpoint"
        },
        // Pending + Released + Missing → converge abandoned.
        {
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Released,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndAbandon,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_released_no_checkpoint"
        },
        // Pending + Committed → Conflict.
        {
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Committed,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_committed_no_dispatch"
        },
        // Pending + Accepted checkpoint → Conflict (checkpoint advanced past gate).
        {
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Missing,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "checkpoint_accepted_but_gate_pending"
        },
        // Pending + Accepted checkpoint → Conflict (checkpoint advanced past gate),
        // regardless of budget state.
        {
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "checkpoint_accepted_but_gate_pending"
        },

        // ── Ready gate ──────────────────────────────────────────────────────────────
        // CW04/CW05: Ready + Reserved + Missing → release budget, abandon.
        {
            AgentToolInvocationPreDispatchState.Ready,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndAbandon,
            (int)AgentToolPreDispatchBudgetAction.FinalizeReleased,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_reserved_no_checkpoint"
        },
        // Ready + Released + Missing → converge abandoned.
        {
            AgentToolInvocationPreDispatchState.Ready,
            AgentToolBudgetReadStatus.Released,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndAbandon,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_released_no_checkpoint"
        },
        // CW07/CW08/CW09: Ready + Reserved + Accepted → release/finalize/publish.
        {
            AgentToolInvocationPreDispatchState.Ready,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndRelease,
            (int)AgentToolPreDispatchBudgetAction.FinalizeReleased,
            (int)AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch,
            "released_no_dispatch"
        },
        // Ready + Released + Accepted → converge governance + release gate.
        {
            AgentToolInvocationPreDispatchState.Ready,
            AgentToolBudgetReadStatus.Released,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndRelease,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch,
            "released_no_dispatch"
        },
        // Ready + Missing + Missing → Conflict (budget missing after bind).
        {
            AgentToolInvocationPreDispatchState.Ready,
            AgentToolBudgetReadStatus.Missing,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_missing_after_bind"
        },
        // Ready + Missing + Accepted → Conflict (budget missing after bind, regardless of checkpoint).
        {
            AgentToolInvocationPreDispatchState.Ready,
            AgentToolBudgetReadStatus.Missing,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_missing_after_bind"
        },

        // ── Accepted gate ───────────────────────────────────────────────────────────
        // §7.9: Accepted + Reserved + Accepted → release/finalize/publish.
        {
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndRelease,
            (int)AgentToolPreDispatchBudgetAction.FinalizeReleased,
            (int)AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch,
            "released_no_dispatch"
        },
        // §7.8: Accepted + Released + Accepted → converge governance + release gate.
        {
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Released,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Released,
            (int)AgentToolPreDispatchGateAction.ClaimAndRelease,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch,
            "released_no_dispatch"
        },
        // Accepted + Missing + Missing → Conflict (budget missing after bind).
        {
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Missing,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_missing_after_bind"
        },
        // Accepted + Missing + Accepted → Conflict (budget missing after bind).
        {
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Missing,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_missing_after_bind"
        },

        // ── Generic conflict / unavailable ──────────────────────────────────────────
        // §7.10: Committed budget → Conflict.
        {
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Committed,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.Conflict,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_committed_no_dispatch"
        },
        // §7.10: Indeterminate budget → StillPending.
        {
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Indeterminate,
            AgentToolGovernancePreDispatchReadStatus.Accepted,
            AgentToolPreDispatchReconciliationStatus.StillPending,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "budget_indeterminate"
        },
        // Authority unavailable → StillPending.
        {
            AgentToolInvocationPreDispatchState.Ready,
            AgentToolBudgetReadStatus.Unknown,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            AgentToolPreDispatchReconciliationStatus.StillPending,
            (int)AgentToolPreDispatchGateAction.None,
            (int)AgentToolPreDispatchBudgetAction.None,
            (int)AgentToolPreDispatchGovernanceAction.None,
            "authority_unavailable"
        }
    };

    [Theory]
    [MemberData(nameof(DecisionMatrix))]
    public void Decide_Returns_Expected_Disposition(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus,
        AgentToolPreDispatchReconciliationStatus expectedDisposition,
        int gateAction,
        int budgetAction,
        int governanceAction,
        string expectedReasonCode)
    {
        var decision = Decide(gateState, budgetStatus, checkpointStatus);

        decision.Disposition.Should().Be(expectedDisposition);
        decision.GateAction.Should().Be((AgentToolPreDispatchGateAction)gateAction);
        decision.BudgetAction.Should().Be((AgentToolPreDispatchBudgetAction)budgetAction);
        decision.GovernanceAction.Should().Be((AgentToolPreDispatchGovernanceAction)governanceAction);
        decision.RequiresOwnershipClaim.Should().Be(
            expectedDisposition == AgentToolPreDispatchReconciliationStatus.Released);
        decision.IsTerminal.Should().Be(expectedDisposition is
            AgentToolPreDispatchReconciliationStatus.Released
            or AgentToolPreDispatchReconciliationStatus.Conflict
            or AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown);
        decision.ReasonCode.Should().Be(expectedReasonCode);
    }

    [Fact]
    public void DispatchStarted_Should_Return_PostDispatchUnknown_WithoutMutation()
    {
        var decision = Decide(
            AgentToolInvocationPreDispatchState.DispatchStarted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        decision.Disposition.Should().Be(AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown);
        decision.ReasonCode.Should().Be("dispatch_started");
        decision.GateAction.Should().Be(AgentToolPreDispatchGateAction.None);
        decision.BudgetAction.Should().Be(AgentToolPreDispatchBudgetAction.None);
        decision.GovernanceAction.Should().Be(AgentToolPreDispatchGovernanceAction.None);
        decision.RequiresOwnershipClaim.Should().BeFalse();
        decision.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(AgentToolInvocationPreDispatchState.CompletionPending)]
    [InlineData(AgentToolInvocationPreDispatchState.Completed)]
    public void PostDispatchGate_Should_Never_Propose_Mutation(AgentToolInvocationPreDispatchState state)
    {
        var decision = Decide(state, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted);

        decision.Disposition.Should().Be(AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown);
        decision.GateAction.Should().Be(AgentToolPreDispatchGateAction.None);
        decision.BudgetAction.Should().Be(AgentToolPreDispatchBudgetAction.None);
        decision.GovernanceAction.Should().Be(AgentToolPreDispatchGovernanceAction.None);
    }

    [Fact]
    public void ReconciliationPending_Should_Compose_On_Preserved_Substate()
    {
        // A claimed attempt drives the decision with its preserved substate, so a
        // reconciler that recovered a prior claim converges the exact same way.
        var gate = Gate(AgentToolInvocationPreDispatchState.ReconciliationPending) with
        {
            ReconciliationClaimedState = AgentToolInvocationPreDispatchState.Accepted,
            ReconciliationClaimToken = "claim-1",
            ReconciliationClaimedAt = DateTimeOffset.UtcNow
        };
        var snapshot = new AgentToolPreDispatchAuthoritySnapshot
        {
            Gate = gate,
            Budget = Budget(AgentToolBudgetReadStatus.Reserved),
            Checkpoint = Checkpoint(AgentToolGovernancePreDispatchReadStatus.Accepted)
        };

        var decision = Policy.Decide(snapshot);

        decision.Disposition.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        decision.GateAction.Should().Be(AgentToolPreDispatchGateAction.ClaimAndRelease);
        decision.BudgetAction.Should().Be(AgentToolPreDispatchBudgetAction.FinalizeReleased);
        decision.GovernanceAction.Should().Be(AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch);
        decision.ReasonCode.Should().Be("released_no_dispatch");
    }

    [Fact]
    public void Every_Released_Disposition_Requires_Ownership_Claim()
    {
        // The Gate remains the single ownership authority: a terminal recovery
        // decision that mutates the Gate must first claim (or recover) ownership.
        foreach (var row in DecisionMatrix)
        {
            var decision = Decide(
                (AgentToolInvocationPreDispatchState)row[0],
                (AgentToolBudgetReadStatus)row[1],
                (AgentToolGovernancePreDispatchReadStatus)row[2]);

            if (decision.Disposition == AgentToolPreDispatchReconciliationStatus.Released)
            {
                decision.RequiresOwnershipClaim.Should().BeTrue(
                    $"Released disposition for gate={row[0]} budget={row[1]} checkpoint={row[2]} must claim ownership");
                decision.GateAction.Should().NotBe(AgentToolPreDispatchGateAction.None);
            }
        }
    }

    private static AgentToolPreDispatchRecoveryDecision Decide(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus)
        => Policy.Decide(new AgentToolPreDispatchAuthoritySnapshot
        {
            Gate = Gate(gateState),
            Budget = Budget(budgetStatus),
            Checkpoint = Checkpoint(checkpointStatus)
        });

    private static AgentToolInvocationPreDispatchResult Gate(AgentToolInvocationPreDispatchState state)
        => new()
        {
            State = state,
            Revision = 1
        };

    private static AgentToolBudgetReservationReadResult Budget(AgentToolBudgetReadStatus status)
        => new() { Status = status };

    private static AgentToolGovernancePreDispatchReadResult Checkpoint(AgentToolGovernancePreDispatchReadStatus status)
        => new() { Status = status };
}
