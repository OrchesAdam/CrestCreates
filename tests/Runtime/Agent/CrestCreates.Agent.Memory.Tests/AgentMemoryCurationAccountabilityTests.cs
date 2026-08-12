using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

/// <summary>
/// Spec §15.3 — Curation Accountability integration. Verifies that the real
/// curation mainline publishes committed facts only after a confirmed
/// conditional commit, records stable rejected/conflict facts for complete
/// typed outcomes, never leaks Reason/Explanation/source content, and never
/// lets projection or publication failures alter the confirmed outcome.
/// </summary>
public sealed class AgentMemoryCurationAccountabilityTests
{
    private const string TenantId = "tenant-1";

    private sealed class CapturedCuration
    {
        public AgentMemoryCurationAccountabilityPayload? Payload { get; set; }

        public AgentMemoryOperationIdentity? Identity { get; set; }

        public AgentMemoryInvocationContext? Context { get; set; }
    }

    /// <summary>
    /// A producer mock that captures the published identity/context/payload and
    /// returns a completed ValueTask by default (Moq default for ValueTask).
    /// </summary>
    private static (Mock<IAgentMemoryAccountabilityProducer> Mock, CapturedCuration Captures) MakeCapturingProducer()
    {
        var captures = new CapturedCuration();
        var mock = new Mock<IAgentMemoryAccountabilityProducer>();
        mock.Setup(p => p.PublishCurationAsync(
                It.IsAny<AgentMemoryOperationIdentity>(),
                It.IsAny<AgentMemoryInvocationContext>(),
                It.IsAny<AgentMemoryCurationAccountabilityPayload>()))
            .Callback<AgentMemoryOperationIdentity, AgentMemoryInvocationContext, AgentMemoryCurationAccountabilityPayload>(
                (id, ctx, pl) =>
                {
                    captures.Identity = id;
                    captures.Context = ctx;
                    captures.Payload = pl;
                });
        return (mock, captures);
    }

    private static AgentMemoryCandidate MakeDummyCandidate(string candidateId)
        => new()
        {
            CandidateId = candidateId,
            TenantId = TenantId,
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{candidateId}",
            CanonicalContentHash = new CanonicalHash
            {
                Value = $"hash-{candidateId}",
                Algorithm = "SHA-256",
                AlgorithmVersion = "v1",
                ArtifactKind = "test",
                Scope = "test",
                Purpose = "test",
                ContractVersion = "v1",
                CanonicalShapeVersion = "v1"
            }
        };

    private static AgentMemoryPromotionPlan MakePromotionPlan(
        AgentMemoryCandidate candidate,
        AgentMemoryCanonicalHashProjector hashes,
        AgentMemoryOperationRequest operation,
        string memoryId,
        CanonicalHash? contentHash = null,
        CanonicalHash? stateHash = null)
    {
        var expected = MemoryTestFixture.CreateExpectedPromotedMemory(candidate, memoryId, operation);
        return new AgentMemoryPromotionPlan
        {
            Candidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = candidate.CandidateId,
                ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
            },
            NewMemoryId = memoryId,
            ExpectedMemoryContentHash = contentHash ?? candidate.CanonicalContentHash,
            ExpectedMemoryStateHash = stateHash ?? hashes.ComputeMemoryStateHash(expected),
            Operation = operation
        };
    }

    private static Mock<IAgentMemoryConditionalCurationStore> MakeConditionalStoreMock()
    {
        var store = new Mock<IAgentMemoryConditionalCurationStore>();
        store.As<IAgentMemoryStore>();
        return store;
    }

    private static DefaultAgentMemoryPromotionService MakePromotionService(
        Mock<IAgentMemoryConditionalCurationStore> store,
        Mock<IAgentMemoryAccountabilityProducer> producer)
        => new(
            (IAgentMemoryStore)store.Object,
            MemoryTestFixture.CreateTestHashProjector(),
            producer: producer.Object,
            factProjector: new AgentMemoryCurationFactProjector());

    private static AgentMemoryItem MakeCommittedItem(string memoryId)
        => new()
        {
            MemoryId = memoryId,
            TenantId = TenantId,
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{memoryId}",
            CanonicalContentHash = MakeDummyCandidate("c-1").CanonicalContentHash,
            PromotedAt = DateTimeOffset.UtcNow,
            Status = AgentMemoryStatus.Active
        };

    [Fact]
    public async Task PromoteCommitted_Should_Record()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-1");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(candidate, hashes, operation, "m-1");

        var committed = await promotion.PromoteAsync(TenantId, plan);

        var payload = captures.Payload;
        var identity = captures.Identity;
        var context = captures.Context;

        committed.MemoryId.Should().Be("m-1");
        payload.Should().NotBeNull();
        payload!.Operation.Should().Be("promote");
        payload.Result.Should().Be("committed");
        payload.OperationId.Should().Be(identity!.OperationId);
        payload.OperationId.Should().Be(operation.Identity.OperationId);
        payload.CandidateId.Should().Be("c-1");
        payload.NewMemoryId.Should().Be("m-1");
        payload.ExpectedCandidateStateHash.Should().Be(hashes.ComputeCandidateStateHash(candidate));
        payload.ExpectedMemoryStateHash.Should().NotBeNull();
        payload.ExpectedContentHash.Should().Be(candidate.CanonicalContentHash);
        payload.PreviousState.Should().Be("candidate");
        payload.ResultingState.Should().Be("active");
        payload.StableFailureCode.Should().BeNull();
        payload.Sanitization.Should().NotBeNull();
        context.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectCommitted_Should_Record()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-1");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);

        await promotion.RejectAsync(TenantId, new AgentMemoryCandidateExpectation
        {
            CandidateId = candidate.CandidateId,
            ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
        }, operation);

        var payload = captures.Payload;
        payload.Should().NotBeNull();
        payload!.Operation.Should().Be("reject");
        payload.Result.Should().Be("committed");
        payload.CandidateId.Should().Be("c-1");
        payload.ExpectedCandidateStateHash.Should().Be(hashes.ComputeCandidateStateHash(candidate));
        payload.PreviousState.Should().Be("candidate");
        payload.ResultingState.Should().Be("rejected");
        payload.Sanitization.Should().BeNull();

        var rejected = (await store.GetCandidateAsync(TenantId, "c-1"))!;
        rejected.Status.Should().Be(AgentMemoryStatus.Rejected);
    }

    [Fact]
    public async Task Supersede_Should_Record_OldAndNewIdentity()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var first = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");
        var replacement = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-2");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var newMemory = MemoryTestFixture.CreateExpectedPromotedMemory(replacement, "m-3", operation)
            with { SupersedesMemoryId = first.MemoryId };
        var plan = new AgentMemorySupersessionPlan
        {
            TargetMemory = new AgentMemoryItemExpectation
            {
                MemoryId = first.MemoryId,
                ExpectedStateHash = hashes.ComputeMemoryStateHash(first)
            },
            ReplacementCandidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = replacement.CandidateId,
                ExpectedStateHash = hashes.ComputeCandidateStateHash(replacement)
            },
            NewMemoryId = newMemory.MemoryId,
            ExpectedMemoryContentHash = replacement.CanonicalContentHash,
            ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(newMemory),
            Operation = operation
        };

        var committed = await promotion.SupersedeAsync(TenantId, plan);

        var payload = captures.Payload;
        committed.MemoryId.Should().Be("m-3");
        payload.Should().NotBeNull();
        payload!.Operation.Should().Be("supersede");
        payload.Result.Should().Be("committed");
        payload.MemoryId.Should().Be("m-1");
        payload.ReplacementCandidateId.Should().Be("c-2");
        payload.NewMemoryId.Should().Be("m-3");
        payload.ExpectedMemoryStateHash.Should().Be(hashes.ComputeMemoryStateHash(first));
        payload.ExpectedReplacementStateHash.Should().Be(hashes.ComputeCandidateStateHash(replacement));
        payload.ExpectedContentHash.Should().Be(replacement.CanonicalContentHash);
        payload.PreviousState.Should().Be("active");
        payload.ResultingState.Should().Be("superseded");
        payload.Sanitization.Should().NotBeNull();

        var superseded = (await store.GetMemoryAsync(TenantId, "m-1"))!;
        superseded.Status.Should().Be(AgentMemoryStatus.Superseded);
    }

    [Fact]
    public async Task ArchiveCommitted_Should_Record()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");
        var request = MemoryTestFixture.CreateOperationRequest(TenantId);

        await promotion.ArchiveAsync(TenantId, memory.MemoryId, request);

        var payload = captures.Payload;
        payload.Should().NotBeNull();
        payload!.Operation.Should().Be("archive");
        payload.Result.Should().Be("committed");
        payload.MemoryId.Should().Be("m-1");
        payload.ExpectedMemoryStateHash.Should().Be(hashes.ComputeMemoryStateHash(memory));
        payload.PreviousState.Should().Be("active");
        payload.ResultingState.Should().Be("archived");
        payload.Sanitization.Should().NotBeNull();

        var archived = (await store.GetMemoryAsync(TenantId, "m-1"))!;
        archived.Status.Should().Be(AgentMemoryStatus.Archived);
    }

    [Fact]
    public async Task StateConflict_Should_RecordStableConflict()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-1");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(
            candidate,
            hashes,
            operation,
            "m-1",
            contentHash: hashes.ComputeContentHash(TenantId, Array.Empty<AgentContextSourceRef>(), "different-content"));

        var act = async () => await promotion.PromoteAsync(TenantId, plan);

        await act.Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(ex => ex.Code == AgentMemoryOperationFailureCode.StateConflict);

        var payload = captures.Payload;
        payload.Should().NotBeNull();
        payload!.Operation.Should().Be("promote");
        payload.Result.Should().Be("conflict");
        payload.StableFailureCode.Should().Be("state-conflict");
        payload.CandidateId.Should().Be("c-1");
        payload.NewMemoryId.Should().Be("m-1");
        payload.PreviousState.Should().BeNull();
        payload.ResultingState.Should().BeNull();
    }

    [Fact]
    public async Task IdentityConflict_Should_RecordStableConflict()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");
        var replacement = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-2");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(replacement, hashes, operation, "m-1");

        var act = async () => await promotion.PromoteAsync(TenantId, plan);

        await act.Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(ex => ex.Code == AgentMemoryOperationFailureCode.IdentityConflict);

        var payload = captures.Payload;
        payload.Should().NotBeNull();
        payload!.Operation.Should().Be("promote");
        payload.Result.Should().Be("conflict");
        payload.StableFailureCode.Should().Be("identity-conflict");
        payload.CandidateId.Should().Be("c-2");
        payload.NewMemoryId.Should().Be("m-1");
    }

    [Fact]
    public async Task UnknownStoreFailure_Should_NotClaimDeterministicFailure()
    {
        var (producer, captures) = MakeCapturingProducer();
        var store = MakeConditionalStoreMock();
        store.Setup(s => s.PromoteAsync(
                It.IsAny<string>(),
                It.IsAny<AgentMemoryPromotionPlan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider exploded"));
        var promotion = MakePromotionService(store, producer);
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(MakeDummyCandidate("c-1"), MemoryTestFixture.CreateTestHashProjector(), operation, "m-1");

        var act = async () => await promotion.PromoteAsync(TenantId, plan);

        await act.Should().ThrowAsync<InvalidOperationException>();
        captures.Payload.Should().BeNull();
    }

    [Fact]
    public async Task ReasonAndExplanation_Should_NotReachAuditSink()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-1");
        var operation = new AgentMemoryOperationRequest
        {
            TenantId = TenantId,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = TenantId,
                ActorId = "agent-1",
                ActorKind = "Agent"
            },
            Reason = "SENSITIVE-REASON-abc123",
            Explanation = "SENSITIVE-EXPLANATION-xyz789",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = $"op-{Guid.NewGuid():N}",
                OccurredAt = DateTimeOffset.UtcNow
            }
        };
        var plan = MakePromotionPlan(candidate, hashes, operation, "m-1");

        await promotion.PromoteAsync(TenantId, plan);

        var json = JsonSerializer.Serialize(captures.Payload);
        json.Should().NotContain("SENSITIVE-REASON-abc123");
        json.Should().NotContain("SENSITIVE-EXPLANATION-xyz789");
        json.Should().NotContain("memory-content-c-1");
    }

    [Fact]
    public async Task RecorderFailure_Should_NotChangeCommittedResult()
    {
        var producer = new Mock<IAgentMemoryAccountabilityProducer>();
        producer.Setup(p => p.PublishCurationAsync(
                It.IsAny<AgentMemoryOperationIdentity>(),
                It.IsAny<AgentMemoryInvocationContext>(),
                It.IsAny<AgentMemoryCurationAccountabilityPayload>()))
            .ThrowsAsync(new InvalidOperationException("sink down"));
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-1");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(candidate, hashes, operation, "m-1");

        var committed = await promotion.PromoteAsync(TenantId, plan);

        committed.MemoryId.Should().Be("m-1");
        var stored = (await store.GetMemoryAsync(TenantId, "m-1"))!;
        stored.Status.Should().Be(AgentMemoryStatus.Active);
    }

    [Fact]
    public async Task CancelledBusinessTokenAfterCommit_Should_NotSuppressAttempt()
    {
        var (producer, captures) = MakeCapturingProducer();
        var transitioned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = MakeConditionalStoreMock();
        var committedItem = MakeCommittedItem("m-1");
        store.Setup(s => s.PromoteAsync(
                It.IsAny<string>(),
                It.IsAny<AgentMemoryPromotionPlan>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string tenantId, AgentMemoryPromotionPlan plan, CancellationToken ct) =>
            {
                transitioned.SetResult();
                await proceed.Task;
                return committedItem;
            });
        var promotion = MakePromotionService(store, producer);
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(MakeDummyCandidate("c-1"), MemoryTestFixture.CreateTestHashProjector(), operation, "m-1");

        using var cts = new CancellationTokenSource();
        var promoteTask = promotion.PromoteAsync(TenantId, plan, cts.Token);
        await transitioned.Task;
        cts.Cancel();
        proceed.SetResult();

        var result = await promoteTask;

        result.MemoryId.Should().Be("m-1");
        captures.Payload.Should().NotBeNull();
        captures.Payload!.Result.Should().Be("committed");
    }

    [Fact]
    public async Task CommittedCurationProjectionFailure_Should_NotChangeCommittedResult()
    {
        var (producer, captures) = MakeCapturingProducer();
        var projector = new Mock<AgentMemoryCurationFactProjector>();
        projector.Setup(p => p.PromoteCommitted(
                It.IsAny<AgentMemoryOperationRequest>(),
                It.IsAny<AgentMemoryPromotionPlan>(),
                It.IsAny<AgentMemoryItem>()))
            .Throws(new InvalidOperationException("projection boom"));
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, projector.Object);
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-1");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(candidate, hashes, operation, "m-1");

        var committed = await promotion.PromoteAsync(TenantId, plan);

        committed.MemoryId.Should().Be("m-1");
        var stored = (await store.GetMemoryAsync(TenantId, "m-1"))!;
        stored.Status.Should().Be(AgentMemoryStatus.Active);
        producer.Verify(p => p.PublishCurationAsync(
            It.IsAny<AgentMemoryOperationIdentity>(),
            It.IsAny<AgentMemoryInvocationContext>(),
            It.IsAny<AgentMemoryCurationAccountabilityPayload>()), Times.Never);
    }

    [Fact]
    public async Task CancelledBeforeTransition_Should_NotCommitOrEmitFact()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");
        var request = MemoryTestFixture.CreateOperationRequest(TenantId);

        // The promote setup step publishes its own committed fact; clear the
        // capture so this test observes only the archive attempt.
        captures.Payload = null;
        captures.Identity = null;
        captures.Context = null;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await promotion.ArchiveAsync(TenantId, memory.MemoryId, request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        captures.Payload.Should().BeNull();
        var stored = (await store.GetMemoryAsync(TenantId, "m-1"))!;
        stored.Status.Should().Be(AgentMemoryStatus.Active);
    }

    [Fact]
    public async Task TypedRejection_Should_RecordStableRejection()
    {
        var (producer, captures) = MakeCapturingProducer();
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture(producer.Object, new AgentMemoryCurationFactProjector());
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");
        var candidate = (await store.GetCandidateAsync(TenantId, "c-1"))!;
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var expectation = new AgentMemoryCandidateExpectation
        {
            CandidateId = candidate.CandidateId,
            ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
        };

        var act = async () => await promotion.RejectAsync(TenantId, expectation, operation);

        await act.Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(ex => ex.Code == AgentMemoryOperationFailureCode.InvalidLifecycleState);

        var payload = captures.Payload;
        payload.Should().NotBeNull();
        payload!.Operation.Should().Be("reject");
        payload.Result.Should().Be("rejected");
        payload.StableFailureCode.Should().Be("invalid-lifecycle-state");
        payload.CandidateId.Should().Be("c-1");
    }

    [Fact]
    public async Task IndeterminateProviderCancellation_Should_NotClaimFailure()
    {
        var (producer, captures) = MakeCapturingProducer();
        var store = MakeConditionalStoreMock();
        store.Setup(s => s.PromoteAsync(
                It.IsAny<string>(),
                It.IsAny<AgentMemoryPromotionPlan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var promotion = MakePromotionService(store, producer);
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var plan = MakePromotionPlan(MakeDummyCandidate("c-1"), MemoryTestFixture.CreateTestHashProjector(), operation, "m-1");

        var act = async () => await promotion.PromoteAsync(TenantId, plan);

        await act.Should().ThrowAsync<OperationCanceledException>();
        captures.Payload.Should().BeNull();
    }
}
