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
        await gate.CompleteAsync(acquired.Lease!, outcome);
        await gate.CompleteAsync(acquired.Lease!, outcome);

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

        await gate.PrepareCompletionAsync(acquired.Lease!, Success());
        var pending = await gate.AcquireAsync(Request("fingerprint-a"));
        pending.Status.Should().Be(AgentToolInvocationAcquireStatus.InProgress);

        await gate.PublishCompletionAsync(acquired.Lease!);
        var replay = await gate.AcquireAsync(Request("fingerprint-a"));
        replay.Status.Should().Be(AgentToolInvocationAcquireStatus.Completed);
    }

    [Fact]
    public async Task Acquire_PermanentlyRejectsDifferentFingerprintAfterReleasedAttempt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fingerprint-a"));
        await gate.ReleaseLeaseAsync(acquired.Lease!);
        await gate.ReleaseLeaseAsync(acquired.Lease!);

        var conflict = await gate.AcquireAsync(Request("fingerprint-b"));

        conflict.Status.Should().Be(AgentToolInvocationAcquireStatus.Conflict);
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
        var staleCompletion = () => gate.CompleteAsync(first.Lease!, Success()).AsTask();
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

        retry.Status.Should().Be(AgentToolInvocationAcquireStatus.Indeterminate);
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-07-16T00:00:00Z");

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
