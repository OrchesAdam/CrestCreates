using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Invocation;

public sealed class AgentToolInvocationGateTests
{
    [Fact]
    public async Task Acquire_AllowsOnlyOneConcurrentOwnerAndReplaysCompletedOutcome()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var request = Request("fingerprint-a");

        var acquired = await gate.AcquireAsync(request);
        var concurrent = await gate.AcquireAsync(request);

        acquired.Status.Should().Be(AgentToolInvocationAcquireStatus.Acquired);
        concurrent.Status.Should().Be(AgentToolInvocationAcquireStatus.InProgress);
        (await gate.TryMarkDispatchStartedAsync(acquired.Lease!)).Should().BeTrue();
        var outcome = Success();
        await gate.PrepareCompletionAsync(acquired.Lease!, PrepareRequest(outcome));
        (await gate.PublishCompletionAsync(acquired.Lease!)).State
            .Should().Be(AgentToolInvocationCompletionState.Completed);
        (await gate.PublishCompletionAsync(acquired.Lease!)).State
            .Should().Be(AgentToolInvocationCompletionState.Completed);

        var replay = await gate.AcquireAsync(request);
        replay.Status.Should().Be(AgentToolInvocationAcquireStatus.Completed);
        replay.CompletedOutcome.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task PreparedCompletion_IsInProgressUntilPublished()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fingerprint-a"));
        (await gate.TryMarkDispatchStartedAsync(acquired.Lease!)).Should().BeTrue();

        await gate.PrepareCompletionAsync(acquired.Lease!, PrepareRequest(Success()));
        var pending = await gate.AcquireAsync(Request("fingerprint-a"));
        pending.Status.Should().Be(AgentToolInvocationAcquireStatus.InProgress);
        var pendingState = await gate.GetCompletionStateAsync(acquired.Lease!);
        pendingState.State.Should().Be(AgentToolInvocationCompletionState.CompletionPending);
        pendingState.PreparedAt.Should().NotBeNull();
        pendingState.BudgetReservationId.Should().Be("reservation");
        pendingState.ReasonCode.Should().Be("completed");

        await gate.PublishCompletionAsync(acquired.Lease!);
        var replay = await gate.AcquireAsync(Request("fingerprint-a"));
        replay.Status.Should().Be(AgentToolInvocationAcquireStatus.Completed);
    }

    [Fact]
    public async Task PublishedCompletion_CannotBeChangedToIndeterminate()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var request = Request("fingerprint-a");
        var acquired = await gate.AcquireAsync(request);
        (await gate.TryMarkDispatchStartedAsync(acquired.Lease!)).Should().BeTrue();
        var outcome = Success();
        await gate.PrepareCompletionAsync(acquired.Lease!, PrepareRequest(outcome));
        await gate.PublishCompletionAsync(acquired.Lease!);

        var act = () => gate.MarkIndeterminateAsync(
            acquired.Lease!,
            "late_uncertainty").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        var replay = await gate.AcquireAsync(request);
        replay.Status.Should().Be(AgentToolInvocationAcquireStatus.Completed);
        replay.CompletedOutcome.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task PublishedCompletion_RejectsConflictingPrepareIdentity()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fingerprint-a"));
        (await gate.TryMarkDispatchStartedAsync(acquired.Lease!)).Should().BeTrue();
        var outcome = Success();
        await gate.PrepareCompletionAsync(
            acquired.Lease!,
            PrepareRequest(outcome, auditId: "audit-1", reservationId: "reservation-1"));
        await gate.PublishCompletionAsync(acquired.Lease!);

        var act = () => gate.PrepareCompletionAsync(
            acquired.Lease!,
            PrepareRequest(outcome, auditId: "audit-2", reservationId: "reservation-2")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(AgentToolInvocationOutcomeKind.UnknownTool)]
    [InlineData(AgentToolInvocationOutcomeKind.InvalidRequest)]
    [InlineData(AgentToolInvocationOutcomeKind.GovernanceDenied)]
    [InlineData(AgentToolInvocationOutcomeKind.InProgress)]
    [InlineData(AgentToolInvocationOutcomeKind.InvocationIndeterminate)]
    public async Task PrepareCompletion_RejectsNonPublishableOutcome(
        AgentToolInvocationOutcomeKind kind)
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fingerprint-a"));
        (await gate.TryMarkDispatchStartedAsync(acquired.Lease!)).Should().BeTrue();

        var act = () => gate.PrepareCompletionAsync(
            acquired.Lease!,
            PrepareRequest(Success() with { Kind = kind })).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Acquire_PermanentlyRejectsDifferentFingerprintAfterReleasedAttempt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fingerprint-a"));
        await gate.AbandonUnrecordedLeaseAsync(acquired.Lease!, "pre_dispatch_unrecorded");
        await gate.AbandonUnrecordedLeaseAsync(acquired.Lease!, "pre_dispatch_unrecorded");

        var conflict = await gate.AcquireAsync(Request("fingerprint-b"));

        conflict.Status.Should().Be(AgentToolInvocationAcquireStatus.Conflict);
    }

    [Fact]
    public async Task PreparedRelease_PersistsAuditAndBudgetUntilPublished()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fingerprint-a"));
        var request = new AgentToolInvocationPrepareReleaseRequest
        {
            AuditId = "audit-1",
            BudgetReservationId = "reservation-1",
            ReasonCode = "pre_dispatch_audit_failure"
        };

        await gate.PrepareReleaseAsync(acquired.Lease!, request);
        var pending = await gate.GetReleaseStateAsync(acquired.Lease!);

        pending.State.Should().Be(AgentToolInvocationReleaseState.ReleasePending);
        pending.PreparedAt.Should().NotBeNull();
        pending.AuditId.Should().Be("audit-1");
        pending.BudgetReservationId.Should().Be("reservation-1");
        pending.ReasonCode.Should().Be(request.ReasonCode);
        (await gate.AcquireAsync(Request("fingerprint-a"))).Status
            .Should().Be(AgentToolInvocationAcquireStatus.InProgress);

        var published = await gate.PublishReleaseAsync(acquired.Lease!);
        published.State.Should().Be(AgentToolInvocationReleaseState.Released);
        published.AuditId.Should().Be("audit-1");
        published.BudgetReservationId.Should().Be("reservation-1");
    }

    [Fact]
    public async Task PublishedRelease_CannotBeChangedAndAllowsNextAttempt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var request = Request("fingerprint-a");
        var acquired = await gate.AcquireAsync(request);
        await gate.PrepareReleaseAsync(acquired.Lease!, new AgentToolInvocationPrepareReleaseRequest
        {
            BudgetReservationId = "reservation-1",
            ReasonCode = "pre_dispatch_audit_failure"
        });
        await gate.PublishReleaseAsync(acquired.Lease!);

        var act = () => gate.MarkIndeterminateAsync(
            acquired.Lease!,
            "late_uncertainty").AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        var next = await gate.AcquireAsync(request);
        next.Status.Should().Be(AgentToolInvocationAcquireStatus.Acquired);
        next.Lease!.AttemptId.Should().NotBe(acquired.Lease!.AttemptId);
    }

    [Fact]
    public async Task ExpiredPreDispatchLease_IsFencedAndNewAttemptCanAcquire()
    {
        var time = new ManualTimeProvider();
        var gate = new DevelopmentInMemoryAgentToolInvocationGate(time, TimeSpan.FromSeconds(10));
        var first = await gate.AcquireAsync(Request("fingerprint-a"));
        time.Advance(TimeSpan.FromSeconds(11));

        var second = await gate.AcquireAsync(Request("fingerprint-a"));

        second.Status.Should().Be(AgentToolInvocationAcquireStatus.Acquired);
        second.Lease!.FencingToken.Should().BeGreaterThan(first.Lease!.FencingToken);
        (await gate.TryMarkDispatchStartedAsync(first.Lease!)).Should().BeFalse();
        var staleCompletion = () => gate.PrepareCompletionAsync(first.Lease!, PrepareRequest(Success())).AsTask();
        await staleCompletion.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExpiredPostDispatchLease_BecomesIndeterminateInsteadOfRedispatching()
    {
        var time = new ManualTimeProvider();
        var gate = new DevelopmentInMemoryAgentToolInvocationGate(time, TimeSpan.FromSeconds(10));
        var first = await gate.AcquireAsync(Request("fingerprint-a"));
        (await gate.TryMarkDispatchStartedAsync(first.Lease!)).Should().BeTrue();
        time.Advance(TimeSpan.FromSeconds(11));

        var retry = await gate.AcquireAsync(Request("fingerprint-a"));
        var state = await gate.GetCompletionStateAsync(first.Lease!);

        retry.Status.Should().Be(AgentToolInvocationAcquireStatus.Indeterminate);
        state.State.Should().Be(AgentToolInvocationCompletionState.Indeterminate);
        state.ReasonCode.Should().Be("post_dispatch_lease_expired");
    }

    private static AgentToolInvocationAcquireRequest Request(string fingerprint)
        => new(
            new AgentToolLogicalInvocationKey("tenant", "user", "agent", "execution", "invocation"),
            fingerprint);

    private static AgentToolInvocationOutcome Success()
        => new()
        {
            Kind = AgentToolInvocationOutcomeKind.Succeeded,
            Code = "ok",
            Message = "ok"
        };

    private static AgentToolInvocationPrepareCompletionRequest PrepareRequest(
        AgentToolInvocationOutcome outcome,
        string? auditId = null,
        string reservationId = "reservation")
        => new()
        {
            Outcome = outcome,
            AuditId = auditId,
            BudgetReservationId = reservationId,
            ReasonCode = "completed"
        };

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-07-16T00:00:00Z");

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
