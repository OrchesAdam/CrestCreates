using System.Diagnostics;
using System.Text;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.Options;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Production;

/// <summary>
/// §8 — the real producer maps completed Memory terminal results onto the unified
/// AuditEnvelope contract. Contract violations throw; recorder failures, timeouts,
/// and sink outcomes are observed through bounded safe diagnostics and never change
/// the original Memory result.
/// </summary>
public sealed class AgentMemoryAccountabilityProducerTests
{
    [Fact]
    public async Task Recall_Completed_Should_MapTo_Succeeded_Completed()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);

        harness.Records.Should().HaveCount(1);
        var record = harness.Records[0];
        record.Action!.Kind.Should().Be("agent-memory.recall");
        record.Action!.Name.Should().Be("recall");
        record.Target!.Kind.Should().Be("agent-memory-pack");
        record.Target!.Id.Should().Be(payload.OperationId);
        record.Outcome!.Status.Should().Be(AuditOutcomeStatuses.Succeeded);
        record.Outcome!.Code.Should().Be("completed");
        record.Payload!.Kind.Should().Be("agent-memory.recall.result");
        record.Payload!.Version.Should().Be(1);
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
    }

    [Fact]
    public async Task EmptyRecall_Should_MapTo_Succeeded_Empty()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateRecallPayload(returnedCount: 0);

        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        harness.Records[0].Outcome!.Status.Should().Be(AuditOutcomeStatuses.Succeeded);
        harness.Records[0].Outcome!.Code.Should().Be("empty");
    }

    [Fact]
    public async Task RejectedRecall_Should_MapTo_Rejected_WithStableCode()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateRecallPayload(
            result: "rejected", stableFailureCode: "resource-unavailable");

        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        harness.Records[0].Outcome!.Status.Should().Be(AuditOutcomeStatuses.Rejected);
        harness.Records[0].Outcome!.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task Curation_PromoteCommitted_Should_MapTo_CandidateTarget_Succeeded()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateCurationPayload(
            operation: "promote", result: "committed", candidateId: "candidate-42");

        await harness.Producer.PublishCurationAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        var record = harness.Records[0];
        record.Action!.Kind.Should().Be("agent-memory.promote");
        record.Action!.Name.Should().Be("promote");
        record.Target!.Kind.Should().Be("agent-memory-candidate");
        record.Target!.Id.Should().Be("candidate-42");
        record.Outcome!.Status.Should().Be(AuditOutcomeStatuses.Succeeded);
        record.Outcome!.Code.Should().Be("committed");
    }

    [Fact]
    public async Task Curation_Supersede_Should_MapTo_MemoryTarget()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateCurationPayload(
            operation: "supersede", result: "committed",
            memoryId: "memory-7", replacementCandidateId: "candidate-99", newMemoryId: "memory-8");

        await harness.Producer.PublishCurationAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        var record = harness.Records[0];
        record.Action!.Kind.Should().Be("agent-memory.supersede");
        record.Target!.Kind.Should().Be("agent-memory");
        record.Target!.Id.Should().Be("memory-7");
        record.Outcome!.Code.Should().Be("committed");
    }

    [Fact]
    public async Task Curation_ArchiveCommitted_Should_MapTo_MemoryTarget()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateCurationPayload(
            operation: "archive", result: "committed", memoryId: "memory-3");

        await harness.Producer.PublishCurationAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        var record = harness.Records[0];
        record.Action!.Kind.Should().Be("agent-memory.archive");
        record.Target!.Kind.Should().Be("agent-memory");
        record.Target!.Id.Should().Be("memory-3");
    }

    [Fact]
    public async Task Curation_Conflict_Should_MapTo_Rejected_Conflict()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateCurationPayload(
            operation: "promote", result: "conflict", candidateId: "candidate-1");

        await harness.Producer.PublishCurationAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        var record = harness.Records[0];
        record.Outcome!.Status.Should().Be(AuditOutcomeStatuses.Rejected);
        record.Outcome!.Code.Should().Be("state-conflict");
    }

    [Fact]
    public async Task SourceExpansion_Expanded_Should_MapTo_Succeeded_Expanded()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateSourceExpansionPayload(status: "expanded", sourceId: "source-9");

        await harness.Producer.PublishSourceExpansionAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        var record = harness.Records[0];
        record.Action!.Kind.Should().Be("agent-memory.source-expand");
        record.Action!.Name.Should().Be("source-expand");
        record.Target!.Kind.Should().Be("agent-memory-source");
        record.Target!.Id.Should().Be("source-9");
        record.Outcome!.Status.Should().Be(AuditOutcomeStatuses.Succeeded);
        record.Outcome!.Code.Should().Be("expanded");
    }

    [Fact]
    public async Task SourceExpansion_Redacted_Should_MapTo_Rejected_Redacted()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateSourceExpansionPayload(
            status: "redacted", sanitizationState: "redacted");

        await harness.Producer.PublishSourceExpansionAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        harness.Records[0].Outcome!.Status.Should().Be(AuditOutcomeStatuses.Rejected);
        harness.Records[0].Outcome!.Code.Should().Be("redacted");
    }

    [Fact]
    public async Task SourceExpansion_NotFound_Should_MapTo_Rejected_NotFound()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateSourceExpansionPayload(status: "not-found");

        await harness.Producer.PublishSourceExpansionAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().HaveCount(1);
        harness.Records[0].Outcome!.Status.Should().Be(AuditOutcomeStatuses.Rejected);
        harness.Records[0].Outcome!.Code.Should().Be("not-found");
    }

    [Fact]
    public async Task DuplicateRepublish_Should_ProduceDuplicate()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);
        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);

        harness.Records.Should().HaveCount(1);
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE").Should().BeTrue();
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT").Should().BeFalse();
    }

    [Fact]
    public async Task ChangedFactSameOperation_Should_ProduceConflict()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        var first = AccountabilityTestFixture.CreateRecallPayload(returnedCount: 2);
        var second = AccountabilityTestFixture.CreateRecallPayload(returnedCount: 7);

        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), first);
        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), second);

        harness.Records.Should().HaveCount(1);
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT").Should().BeTrue();
    }

    [Fact]
    public async Task RecorderRejected_Should_LogRecorderRejected_WithoutThrowing()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        // A rejected recall whose stable failure code is missing fails the
        // sanitization rule, so the recorder returns Rejected.
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "rejected",
            StableFailureCode = null,
            EffectivePackHash = null,
            ReturnedCount = 0,
            WasTruncated = false,
            DiagnosticCodes = Array.Empty<string>(),
            RequestedKinds = Array.Empty<string>(),
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };

        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            payload);

        harness.Records.Should().BeEmpty();
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDER_REJECTED").Should().BeTrue();
    }

    [Fact]
    public async Task NoSinkConfigured_Should_LogNoSink()
    {
        var recorder = AccountabilityTestFixture.CreateRecorder(
            Array.Empty<IAuditSink>(),
            AccountabilityTestFixture.CreateHashComputer().Object);
        using var harness = new AccountabilityTestFixture.ProducerHarness(recorder: recorder);

        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            AccountabilityTestFixture.CreateRecallPayload());

        harness.Records.Should().BeEmpty();
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_NO_SINK").Should().BeTrue();
    }

    [Fact]
    public async Task RecorderTimeout_Should_LogTimeout_WithoutThrowing()
    {
        var recorder = new Mock<IAuditRecorder>();
        recorder
            .Setup(r => r.RecordAsync(It.IsAny<AuditEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var harness = new AccountabilityTestFixture.ProducerHarness(recorder: recorder.Object);

        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            AccountabilityTestFixture.CreateRecallPayload());

        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_TIMEOUT").Should().BeTrue();
    }

    [Fact]
    public async Task RecorderIgnoringCancellation_Should_RespectHardWriteDeadline()
    {
        var completion = new TaskCompletionSource<AuditRecordResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new Mock<IAuditRecorder>();
        recorder
            .Setup(r => r.RecordAsync(It.IsAny<AuditEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AuditRecordResult>(completion.Task));
        using var harness = new AccountabilityTestFixture.ProducerHarness(
            options: new AgentMemoryAccountabilityOptions { WriteTimeout = TimeSpan.FromMilliseconds(25) },
            recorder: recorder.Object);

        var stopwatch = Stopwatch.StartNew();
        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            AccountabilityTestFixture.CreateRecallPayload());
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_TIMEOUT").Should().BeTrue();
        completion.TrySetCanceled();
    }

    [Fact]
    public async Task RecorderFailure_Should_LogRecorderFailed_WithoutThrowing()
    {
        var recorder = new Mock<IAuditRecorder>();
        recorder
            .Setup(r => r.RecordAsync(It.IsAny<AuditEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("recorder exploded"));
        using var harness = new AccountabilityTestFixture.ProducerHarness(recorder: recorder.Object);

        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            AccountabilityTestFixture.CreateRecallPayload());

        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDER_FAILED").Should().BeTrue();
    }

    [Fact]
    public async Task OperationIdMismatch_Should_ThrowContractInvalid()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity(operationId: "op-other");
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        var act = () => harness.Producer.PublishRecallAsync(
            identity,
            AccountabilityTestFixture.CreateContext(),
            payload).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_PRODUCER_CONTRACT_INVALID*OperationId mismatch*");
    }

    [Fact]
    public async Task OccurredAtMissing_Should_ThrowContractInvalid()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = new AgentMemoryOperationIdentity
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            OccurredAt = default
        };
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        var act = () => harness.Producer.PublishRecallAsync(
            identity,
            AccountabilityTestFixture.CreateContext(),
            payload).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_PRODUCER_CONTRACT_INVALID*OccurredAt must be supplied*");
    }

    [Fact]
    public async Task Envelope_Should_CarryActorAndRuntimeReferences()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();

        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(),
            AccountabilityTestFixture.CreateRecallPayload());

        var record = harness.Records[0];
        record.Actor!.Kind.Should().Be(AuditActorKinds.Agent);
        record.Actor!.Id.Should().Be(AccountabilityTestFixture.FixedActorId);
        record.Runtime!.InvocationSource.Should().Be(AuditInvocationSources.Agent);
        record.Runtime!.ExecutionId.Should().Be(AccountabilityTestFixture.FixedOperationId);
        record.Runtime!.References.Should().Contain(new AuditRuntimeReference("agent-invocation", AccountabilityTestFixture.FixedInvocationId));
        record.Runtime!.References.Should().Contain(new AuditRuntimeReference("agent-session", AccountabilityTestFixture.FixedSessionId));
    }

    [Fact]
    public async Task Envelope_Should_NotPersistDisplayName()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        await harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext() with { DisplayName = "private display name" },
            AccountabilityTestFixture.CreateRecallPayload());

        harness.Records[0].Actor!.DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task MissingCorrelation_Should_ThrowContractInvalid()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var act = () => harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(correlationId: null),
            AccountabilityTestFixture.CreateRecallPayload()).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_PRODUCER_CONTRACT_INVALID*correlation context*");
    }

    [Fact]
    public async Task UnknownActorKind_Should_ThrowContractInvalid()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var act = () => harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext() with { ActorKind = "custom-user" },
            AccountabilityTestFixture.CreateRecallPayload()).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_PRODUCER_CONTRACT_INVALID*stable mappings*");
    }

    [Fact]
    public async Task MissingInvocationSource_Should_ThrowContractInvalid()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var act = () => harness.Producer.PublishRecallAsync(
            AccountabilityTestFixture.CreateIdentity(),
            AccountabilityTestFixture.CreateContext(invocationSource: null),
            AccountabilityTestFixture.CreateRecallPayload()).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AGENT_MEMORY_ACCOUNTABILITY_PRODUCER_CONTRACT_INVALID*trusted tenant*");
    }
}
