using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

public sealed class AgentToolBudgetGateTests
{
    [Fact]
    public async Task ConcurrentReserve_ForSameAttempt_ReturnsOneReservation()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var request = new AgentToolBudgetReserveRequest
        {
            Context = GovernanceTestData.Context(maxCalls: 2)
        };
        using var barrier = new Barrier(3);

        var firstTask = ReserveAfterBarrierAsync();
        var secondTask = ReserveAfterBarrierAsync();
        barrier.SignalAndWait();
        var results = await Task.WhenAll(firstTask, secondTask);

        results.Should().OnlyContain(result =>
            result.Status == AgentToolBudgetReserveStatus.Reserved);
        results.Select(result => result.Reservation!.ReservationId)
            .Distinct(StringComparer.Ordinal).Should().ContainSingle();

        async Task<AgentToolBudgetReserveResult> ReserveAfterBarrierAsync()
        {
            return await Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await gate.ReserveAsync(request);
            });
        }
    }

    [Fact]
    public async Task ReleasedAttempt_AllowsNewAttemptAndReturnsCapacity()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var firstContext = GovernanceTestData.Context(maxCalls: 1);
        var first = await ReserveAsync(gate, firstContext);
        await gate.FinalizeAsync(Finalize(
            first,
            AgentToolBudgetReservationState.Released));

        var second = await gate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = GovernanceTestData.Context(attemptId: "attempt-2", maxCalls: 1)
        });

        second.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Reservation!.ReservationId.Should().NotBe(first.ReservationId);
    }

    [Fact]
    public async Task ReservedAndCommitted_OccupyExecutionCategoryCapacity()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var firstContext = GovernanceTestData.Context(maxCalls: 1);
        var first = await ReserveAsync(gate, firstContext);
        var competingContext = GovernanceTestData.Context(
            attemptId: "attempt-2",
            fingerprint: "fingerprint-2",
            invocationId: "invocation-2",
            maxCalls: 1);

        var whileReserved = await gate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = competingContext
        });
        await gate.FinalizeAsync(Finalize(
            first,
            AgentToolBudgetReservationState.Committed));
        var afterCommit = await gate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = competingContext
        });

        whileReserved.ReasonCode.Should().Be("budget_capacity_exceeded");
        afterCommit.ReasonCode.Should().Be("budget_capacity_exceeded");
    }

    [Fact]
    public async Task SameCategory_DifferentTools_HaveIndependentCallCapacity()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        await ReserveAsync(gate, GovernanceTestData.Context(maxCalls: 1));
        var otherToolContext = GovernanceTestData.Context(
            attemptId: "attempt-2",
            fingerprint: "fingerprint-2",
            invocationId: "invocation-2",
            maxCalls: 1) with
        {
            ToolContract = new AgentToolContractIdentity(
                "tool-2",
                1,
                "tool-2-contract-hash")
        };

        var otherTool = await gate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = otherToolContext
        });

        otherTool.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
    }

    [Theory]
    [InlineData(AgentToolBudgetReservationState.Committed)]
    [InlineData(AgentToolBudgetReservationState.Indeterminate)]
    public async Task TerminalLogicalInvocation_BlocksAnotherAttempt(
        AgentToolBudgetReservationState terminalState)
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var first = await ReserveAsync(gate, GovernanceTestData.Context(maxCalls: 2));
        await gate.FinalizeAsync(Finalize(first, terminalState));

        var retry = await gate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = GovernanceTestData.Context(attemptId: "attempt-2", maxCalls: 2)
        });

        retry.Status.Should().Be(AgentToolBudgetReserveStatus.Denied);
        retry.ReasonCode.Should().Be(terminalState == AgentToolBudgetReservationState.Committed
            ? "budget_logical_invocation_committed"
            : "budget_logical_invocation_indeterminate");
    }

    [Fact]
    public async Task Finalize_IsIdempotentAndTerminalStateCannotChange()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var reservation = await ReserveAsync(gate, GovernanceTestData.Context());
        var request = Finalize(reservation, AgentToolBudgetReservationState.Committed);

        var first = await gate.FinalizeAsync(request);
        var replay = await gate.FinalizeAsync(request);
        var conflicting = async () => await gate.FinalizeAsync(Finalize(
            reservation,
            AgentToolBudgetReservationState.Released));

        replay.Should().Be(first);
        await conflicting.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<AgentToolBudgetReservation> ReserveAsync(
        IAgentToolBudgetGate gate,
        AgentToolGovernanceContext context)
    {
        var result = await gate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = context
        });
        result.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        return result.Reservation!;
    }

    private static AgentToolBudgetFinalizeRequest Finalize(
        AgentToolBudgetReservation reservation,
        AgentToolBudgetReservationState state)
        => new()
        {
            ReservationId = reservation.ReservationId,
            AttemptId = reservation.AttemptId,
            InvocationFingerprint = reservation.InvocationFingerprint,
            RequestedState = state,
            ReasonCode = "test_settlement"
        };
}
