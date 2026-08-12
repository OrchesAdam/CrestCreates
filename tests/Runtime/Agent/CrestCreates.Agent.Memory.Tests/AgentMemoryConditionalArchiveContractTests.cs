using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

/// <summary>
/// Spec §15.3 — conditional Archive contract on the Store. The canonical
/// service resolves the current item, computes its existing state hash through
/// the shared projector, and invokes the conditional transition. The Store
/// atomically verifies Active/Superseded plus the expectation and writes
/// Archived. No Accountability type crosses the Store boundary.
/// </summary>
public sealed class AgentMemoryConditionalArchiveContractTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public async Task CancelledPromoteBeforeTransition_Should_NotCommit()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-promote-cancel");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var memory = MemoryTestFixture.CreateExpectedPromotedMemory(candidate, "m-promote-cancel", operation);
        var plan = new AgentMemoryPromotionPlan
        {
            Candidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = candidate.CandidateId,
                ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
            },
            NewMemoryId = memory.MemoryId,
            ExpectedMemoryContentHash = candidate.CanonicalContentHash,
            ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(memory),
            Operation = operation
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Invoking(() => store.PromoteAsync(TenantId, plan, cts.Token).AsTask())
            .Should().ThrowAsync<OperationCanceledException>();
        (await store.GetCandidateAsync(TenantId, candidate.CandidateId))!.Status.Should().Be(AgentMemoryStatus.Candidate);
        (await store.GetMemoryAsync(TenantId, memory.MemoryId)).Should().BeNull();
    }

    [Fact]
    public async Task CancelledRejectBeforeTransition_Should_NotCommit()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var candidate = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-reject-cancel");
        var expectation = new AgentMemoryCandidateExpectation
        {
            CandidateId = candidate.CandidateId,
            ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Invoking(() => store.RejectAsync(TenantId, expectation, MemoryTestFixture.CreateOperationRequest(TenantId), cts.Token).AsTask())
            .Should().ThrowAsync<OperationCanceledException>();
        (await store.GetCandidateAsync(TenantId, candidate.CandidateId))!.Status.Should().Be(AgentMemoryStatus.Candidate);
    }

    [Fact]
    public async Task CancelledSupersedeBeforeTransition_Should_NotCommit()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var target = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-target-cancel", "m-target-cancel");
        var replacement = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-replacement-cancel");
        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var memory = MemoryTestFixture.CreateExpectedPromotedMemory(replacement, "m-replacement-cancel", operation) with
        {
            SupersedesMemoryId = target.MemoryId
        };
        var plan = new AgentMemorySupersessionPlan
        {
            TargetMemory = new AgentMemoryItemExpectation
            {
                MemoryId = target.MemoryId,
                ExpectedStateHash = hashes.ComputeMemoryStateHash(target)
            },
            ReplacementCandidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = replacement.CandidateId,
                ExpectedStateHash = hashes.ComputeCandidateStateHash(replacement)
            },
            NewMemoryId = memory.MemoryId,
            ExpectedMemoryContentHash = replacement.CanonicalContentHash,
            ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(memory),
            Operation = operation
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Invoking(() => store.SupersedeAsync(TenantId, plan, cts.Token).AsTask())
            .Should().ThrowAsync<OperationCanceledException>();
        (await store.GetMemoryAsync(TenantId, target.MemoryId))!.Status.Should().Be(AgentMemoryStatus.Active);
        (await store.GetCandidateAsync(TenantId, replacement.CandidateId))!.Status.Should().Be(AgentMemoryStatus.Candidate);
        (await store.GetMemoryAsync(TenantId, memory.MemoryId)).Should().BeNull();
    }

    [Fact]
    public async Task Active_Should_ArchiveAtomically()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");

        var expectation = new AgentMemoryItemExpectation
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = hashes.ComputeMemoryStateHash(memory)
        };
        var request = MemoryTestFixture.CreateOperationRequest(TenantId);

        await store.ArchiveAsync(TenantId, expectation, request);

        var archived = (await store.GetMemoryAsync(TenantId, memory.MemoryId))!;
        archived.Status.Should().Be(AgentMemoryStatus.Archived);
    }

    [Fact]
    public async Task Superseded_Should_ArchiveAtomically()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();

        var first = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");
        var replacement = await MemoryTestFixture.CreateCandidateAsync(store, hashes, TenantId, "c-2");

        var operation = MemoryTestFixture.CreateOperationRequest(TenantId);
        var newMemory = MemoryTestFixture.CreateExpectedPromotedMemory(replacement, "m-3", operation)
            with { SupersedesMemoryId = first.MemoryId };
        var supersededPlan = new AgentMemorySupersessionPlan
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
        await promotion.SupersedeAsync(TenantId, supersededPlan);

        var superseded = (await store.GetMemoryAsync(TenantId, first.MemoryId))!;
        superseded.Status.Should().Be(AgentMemoryStatus.Superseded);

        var expectation = new AgentMemoryItemExpectation
        {
            MemoryId = superseded.MemoryId,
            ExpectedStateHash = hashes.ComputeMemoryStateHash(superseded)
        };
        await store.ArchiveAsync(TenantId, expectation, MemoryTestFixture.CreateOperationRequest(TenantId));

        var archived = (await store.GetMemoryAsync(TenantId, first.MemoryId))!;
        archived.Status.Should().Be(AgentMemoryStatus.Archived);
    }

    [Fact]
    public async Task StateHashMismatch_Should_Conflict()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");

        var other = memory with { Content = "tampered" };
        var expectation = new AgentMemoryItemExpectation
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = hashes.ComputeMemoryStateHash(other)
        };

        var act = async () => await store.ArchiveAsync(TenantId, expectation, MemoryTestFixture.CreateOperationRequest(TenantId));

        var exception = await act.Should().ThrowAsync<AgentMemoryOperationException>();
        exception.Which.Code.Should().Be(AgentMemoryOperationFailureCode.StateConflict);
        (await store.GetMemoryAsync(TenantId, memory.MemoryId))!.Status.Should().Be(AgentMemoryStatus.Active);
    }

    [Fact]
    public async Task ConcurrentArchive_Should_HaveOneCommittedTransition()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");

        var expectation = new AgentMemoryItemExpectation
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = hashes.ComputeMemoryStateHash(memory)
        };
        var request = MemoryTestFixture.CreateOperationRequest(TenantId);

        async Task<bool> AttemptAsync()
        {
            try
            {
                await store.ArchiveAsync(TenantId, expectation, request);
                return true;
            }
            catch (AgentMemoryOperationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        results.Count(success => success).Should().Be(1, "only one concurrent archive may commit the transition");

        var final = (await store.GetMemoryAsync(TenantId, memory.MemoryId))!;
        final.Status.Should().Be(AgentMemoryStatus.Archived);
    }

    [Fact]
    public async Task CancelledBeforeTransition_Should_NotCommit()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");

        var expectation = new AgentMemoryItemExpectation
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = hashes.ComputeMemoryStateHash(memory)
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await store.ArchiveAsync(TenantId, expectation, MemoryTestFixture.CreateOperationRequest(TenantId), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetMemoryAsync(TenantId, memory.MemoryId))!.Status.Should().Be(AgentMemoryStatus.Active);
    }

    [Fact]
    public async Task Store_Should_NotReceiveAccountabilityTypes()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");

        var expectation = new AgentMemoryItemExpectation
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = hashes.ComputeMemoryStateHash(memory)
        };

        await store.ArchiveAsync(TenantId, expectation, MemoryTestFixture.CreateOperationRequest(TenantId));

        var storeType = store.GetType();
        var receivesAccountabilityType = storeType.GetMethods()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Any(IsAccountabilityType);
        receivesAccountabilityType.Should().BeFalse();
    }

    private static bool IsAccountabilityType(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name;
        return assemblyName is not null
            && assemblyName.StartsWith("CrestCreates.Accountability", StringComparison.Ordinal);
    }
}
