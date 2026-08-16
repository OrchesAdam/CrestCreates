using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Curation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests.Curation;

public sealed class AgentMemoryCurationStateMachineTests
{
    private readonly TestStateHashProjector _hashes = new();
    private readonly DefaultAgentMemoryCurationStateMachine _machine;

    public AgentMemoryCurationStateMachineTests()
    {
        _machine = new DefaultAgentMemoryCurationStateMachine(_hashes, new DefaultAgentMemoryCurationProjector());
    }

    // ── Promote ──────────────────────────────────────────────────────────────

    [Fact]
    public void Promote_Should_ReturnActiveCandidateAndActiveMemory()
    {
        var candidate = Candidate("c-1");
        var plan = PromotePlan(candidate, "m-1");

        var mutation = _machine.PreparePromote("tenant-1", candidate, plan);

        mutation.Candidate.Status.Should().Be(AgentMemoryStatus.Active);
        mutation.Memory.MemoryId.Should().Be("m-1");
        mutation.Memory.Status.Should().Be(AgentMemoryStatus.Active);
        mutation.Memory.IsAuthoritative.Should().BeFalse();
        mutation.Memory.PromotedAt.Should().Be(plan.Operation.Identity.OccurredAt);
    }

    [Fact]
    public void Promote_WithTenantMismatch_Should_FailTenantMismatch()
    {
        var candidate = Candidate("c-1");
        var plan = PromotePlan(candidate, "m-1");

        var act = () => _machine.PreparePromote("tenant-OTHER", candidate, plan);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.TenantMismatch);
    }

    [Fact]
    public void Promote_WithNonCandidateStatus_Should_FailInvalidLifecycleState()
    {
        var candidate = Candidate("c-1") with { Status = AgentMemoryStatus.Rejected };
        var plan = PromotePlan(candidate, "m-1");

        var act = () => _machine.PreparePromote("tenant-1", candidate, plan);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.InvalidLifecycleState);
    }

    [Fact]
    public void Promote_WithStaleCandidateHash_Should_FailStateConflict()
    {
        var candidate = Candidate("c-1");
        var plan = PromotePlan(candidate, "m-1") with
        {
            Candidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = candidate.CandidateId,
                ExpectedStateHash = _hashes.Tamper(_hashes.ComputeCandidateStateHash(candidate))
            }
        };

        var act = () => _machine.PreparePromote("tenant-1", candidate, plan);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.StateConflict);
    }

    [Fact]
    public void Promote_WithContentHashMismatch_Should_FailStateConflict()
    {
        var candidate = Candidate("c-1");
        var plan = PromotePlan(candidate, "m-1") with
        {
            ExpectedMemoryContentHash = _hashes.Tamper(candidate.CanonicalContentHash)
        };

        var act = () => _machine.PreparePromote("tenant-1", candidate, plan);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.StateConflict);
    }

    [Fact]
    public void Promote_WithExpectedNewStateHashDrift_Should_FailStateConflict()
    {
        var candidate = Candidate("c-1");
        var plan = PromotePlan(candidate, "m-1");
        var drifted = plan with
        {
            ExpectedMemoryStateHash = _hashes.Tamper(plan.ExpectedMemoryStateHash)
        };

        var act = () => _machine.PreparePromote("tenant-1", candidate, drifted);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.StateConflict);
    }

    // ── Reject ───────────────────────────────────────────────────────────────

    [Fact]
    public void Reject_Should_ReturnRejectedCandidate()
    {
        var candidate = Candidate("c-1");
        var expectation = Expectation(candidate);

        var mutation = _machine.PrepareReject("tenant-1", candidate, expectation);

        mutation.Candidate.Status.Should().Be(AgentMemoryStatus.Rejected);
    }

    [Fact]
    public void Reject_WithStaleExpectation_Should_FailStateConflict()
    {
        var candidate = Candidate("c-1");
        var expectation = Expectation(candidate) with
        {
            ExpectedStateHash = _hashes.Tamper(_hashes.ComputeCandidateStateHash(candidate))
        };

        var act = () => _machine.PrepareReject("tenant-1", candidate, expectation);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.StateConflict);
    }

    // ── Supersede ────────────────────────────────────────────────────────────

    [Fact]
    public void Supersede_Should_ReturnThreeNodeGraph()
    {
        var target = ActiveMemory("m-old");
        var replacement = Candidate("c-replacement");
        var plan = SupersedePlan(target, replacement, "m-new");

        var mutation = _machine.PrepareSupersede("tenant-1", target, replacement, plan);

        mutation.SupersededMemory.MemoryId.Should().Be("m-old");
        mutation.SupersededMemory.Status.Should().Be(AgentMemoryStatus.Superseded);
        mutation.SupersededMemory.SupersededByMemoryId.Should().Be("m-new");
        mutation.SupersedingMemory.MemoryId.Should().Be("m-new");
        mutation.SupersedingMemory.Status.Should().Be(AgentMemoryStatus.Active);
        mutation.SupersedingMemory.SupersedesMemoryId.Should().Be("m-old");
        mutation.ReplacementCandidate.Status.Should().Be(AgentMemoryStatus.Active);
    }

    [Fact]
    public void Supersede_WithNonActiveTarget_Should_FailInvalidLifecycleState()
    {
        var target = ActiveMemory("m-old") with { Status = AgentMemoryStatus.Archived };
        var replacement = Candidate("c-replacement");
        var plan = SupersedePlan(target, replacement, "m-new");

        var act = () => _machine.PrepareSupersede("tenant-1", target, replacement, plan);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.InvalidLifecycleState);
    }

    [Fact]
    public void Supersede_WithNonCandidateReplacement_Should_FailInvalidLifecycleState()
    {
        var target = ActiveMemory("m-old");
        var replacement = Candidate("c-replacement") with { Status = AgentMemoryStatus.Rejected };
        var plan = SupersedePlan(target, replacement, "m-new");

        var act = () => _machine.PrepareSupersede("tenant-1", target, replacement, plan);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.InvalidLifecycleState);
    }

    [Fact]
    public void Supersede_WithSelfLink_Should_FailStateConflict()
    {
        var target = ActiveMemory("m-old");
        var replacement = Candidate("c-replacement");
        var plan = SupersedePlan(target, replacement, "m-old");

        var act = () => _machine.PrepareSupersede("tenant-1", target, replacement, plan);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.StateConflict);
    }

    // ── Archive ──────────────────────────────────────────────────────────────

    [Fact]
    public void Archive_Should_AcceptActiveAndSuperseded()
    {
        foreach (var status in new[] { AgentMemoryStatus.Active, AgentMemoryStatus.Superseded })
        {
            var memory = ActiveMemory("m-1") with { Status = status };
            var expectation = MemoryExpectation(memory);

            var mutation = _machine.PrepareArchive("tenant-1", memory, expectation);

            mutation.Memory.Status.Should().Be(AgentMemoryStatus.Archived);
            mutation.Memory.SupersedesMemoryId.Should().Be(memory.SupersedesMemoryId);
            mutation.Memory.SupersededByMemoryId.Should().Be(memory.SupersededByMemoryId);
        }
    }

    [Theory]
    [InlineData(AgentMemoryStatus.Candidate)]
    [InlineData(AgentMemoryStatus.Rejected)]
    [InlineData(AgentMemoryStatus.Archived)]
    public void Archive_Should_RejectIllegalSourceStates(AgentMemoryStatus status)
    {
        var memory = ActiveMemory("m-1") with { Status = status };
        var expectation = MemoryExpectation(memory);

        var act = () => _machine.PrepareArchive("tenant-1", memory, expectation);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.InvalidLifecycleState);
    }

    [Fact]
    public void Archive_WithStaleHash_Should_FailStateConflict()
    {
        var memory = ActiveMemory("m-1");
        var expectation = MemoryExpectation(memory) with
        {
            ExpectedStateHash = _hashes.Tamper(_hashes.ComputeMemoryStateHash(memory))
        };

        var act = () => _machine.PrepareArchive("tenant-1", memory, expectation);

        act.Should().Throw<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.StateConflict);
    }

    // ── ownership: no resource-existence results ─────────────────────────────

    [Fact]
    public void StateMachine_Should_Never_ProduceResourceUnavailableOrIdentityConflict()
    {
        // The state machine owns only Tenant/lifecycle/state/projection checks;
        // Store contract cases produce ResourceUnavailable/IdentityConflict.
        var candidate = Candidate("c-1");
        var plan = PromotePlan(candidate, "m-1");
        var outcomes = new List<AgentMemoryOperationFailureCode>();
        var target = ActiveMemory("m-old");
        var replacement = Candidate("c-replacement");

        foreach (var action in new Action[]
        {
            () => _machine.PreparePromote("tenant-1", candidate, plan),
            () => _machine.PrepareReject("tenant-1", candidate, Expectation(candidate)),
            () => _machine.PrepareSupersede("tenant-1", target, replacement, SupersedePlan(target, replacement, "m-new")),
            () => _machine.PrepareArchive("tenant-1", ActiveMemory("m-1"), MemoryExpectation(ActiveMemory("m-1")))
        })
        {
            try
            {
                action();
            }
            catch (AgentMemoryOperationException exception)
            {
                outcomes.Add(exception.Code);
            }
        }

        outcomes.Should().NotContain(AgentMemoryOperationFailureCode.ResourceUnavailable);
        outcomes.Should().NotContain(AgentMemoryOperationFailureCode.IdentityConflict);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private AgentMemoryCandidate Candidate(string candidateId)
        => new()
        {
            CandidateId = candidateId,
            TenantId = "tenant-1",
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{candidateId}",
            CanonicalContentHash = TestHash($"candidate-{candidateId}"),
            Confidence = AgentMemoryConfidence.Medium
        };

    private AgentMemoryItem ActiveMemory(string memoryId)
        => new()
        {
            MemoryId = memoryId,
            TenantId = "tenant-1",
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{memoryId}",
            CanonicalContentHash = TestHash($"memory-{memoryId}"),
            PromotedAt = DateTimeOffset.UnixEpoch,
            Confidence = AgentMemoryConfidence.Medium,
            Status = AgentMemoryStatus.Active
        };

    private AgentMemoryCandidateExpectation Expectation(AgentMemoryCandidate candidate)
        => new()
        {
            CandidateId = candidate.CandidateId,
            ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate)
        };

    private AgentMemoryItemExpectation MemoryExpectation(AgentMemoryItem memory)
        => new()
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = _hashes.ComputeMemoryStateHash(memory)
        };

    private AgentMemoryPromotionPlan PromotePlan(AgentMemoryCandidate candidate, string newMemoryId)
    {
        var memory = new DefaultAgentMemoryCurationProjector()
            .ProjectPromotedMemory(candidate, newMemoryId, Operation());
        return new AgentMemoryPromotionPlan
        {
            Candidate = Expectation(candidate),
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = candidate.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(memory),
            Operation = Operation()
        };
    }

    private AgentMemorySupersessionPlan SupersedePlan(AgentMemoryItem target, AgentMemoryCandidate replacement, string newMemoryId)
    {
        var superseding = new DefaultAgentMemoryCurationProjector()
            .ProjectSupersedingMemory(replacement, target.MemoryId, newMemoryId, Operation());
        return new AgentMemorySupersessionPlan
        {
            TargetMemory = MemoryExpectation(target),
            ReplacementCandidate = Expectation(replacement),
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = replacement.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(superseding),
            Operation = Operation()
        };
    }

    private static AgentMemoryOperationRequest Operation()
        => new()
        {
            TenantId = "tenant-1",
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = "tenant-1",
                ActorId = "actor-1",
                ActorKind = "system",
                CorrelationId = "correlation-1",
                InvocationSource = "system"
            },
            Reason = "test",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = "op-1",
                OccurredAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero)
            },
            Explanation = "test"
        };

    private static CanonicalHash TestHash(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AgentMemoryTest",
            Scope = "InternalFull",
            Purpose = "Test",
            ContractVersion = "memory-hash-v1",
            CanonicalShapeVersion = "test-v1"
        };

    private sealed class TestStateHashProjector : IAgentMemoryStateHashProjector
    {
        public CanonicalHash ComputeCandidateStateHash(AgentMemoryCandidate candidate)
            => new()
            {
                Value = $"candidate-state-{candidate.CandidateId}-{candidate.Status}",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "AgentMemoryTest",
                Scope = "InternalFull",
                Purpose = "Test",
                ContractVersion = "memory-hash-v1",
                CanonicalShapeVersion = "test-v1"
            };

        public CanonicalHash ComputeMemoryStateHash(AgentMemoryItem memory)
            => new()
            {
                Value = $"memory-state-{memory.MemoryId}-{memory.Status}",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "AgentMemoryTest",
                Scope = "InternalFull",
                Purpose = "Test",
                ContractVersion = "memory-hash-v1",
                CanonicalShapeVersion = "test-v1"
            };

        public CanonicalHash Tamper(CanonicalHash hash) => hash with { Value = hash.Value + "-tampered" };
    }
}
