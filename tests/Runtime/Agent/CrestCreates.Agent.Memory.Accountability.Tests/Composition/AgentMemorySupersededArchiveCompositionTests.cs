using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Composition;

public sealed class AgentMemorySupersededArchiveCompositionTests
{
    [Fact]
    public async Task ArchiveSuperseded_Should_RecordCommittedAccountabilityFact()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var hashes = new AgentMemoryCanonicalHashProjector(AccountabilityTestFixture.CreateHashComputer().Object);
        var store = new InMemoryAgentMemoryStore(hashes);
        var promotion = new DefaultAgentMemoryPromotionService(
            store,
            hashes,
            producer: harness.Producer,
            factProjector: new AgentMemoryCurationFactProjector());

        var firstCandidate = CreateCandidate(hashes, "candidate-1", "first");
        await store.CreateCandidateAsync(firstCandidate);
        var firstOperation = CreateOperation(firstCandidate.TenantId);
        var firstMemory = CreateMemory(firstCandidate, "memory-1", firstOperation);
        await promotion.PromoteAsync(firstCandidate.TenantId, CreatePromotionPlan(hashes, firstCandidate, firstMemory, firstOperation));

        var replacementCandidate = CreateCandidate(hashes, "candidate-2", "replacement");
        await store.CreateCandidateAsync(replacementCandidate);
        var supersedeOperation = CreateOperation(firstCandidate.TenantId);
        var replacementMemory = CreateMemory(replacementCandidate, "memory-2", supersedeOperation) with
        {
            SupersedesMemoryId = firstMemory.MemoryId
        };
        await promotion.SupersedeAsync(firstCandidate.TenantId, new AgentMemorySupersessionPlan
        {
            TargetMemory = new AgentMemoryItemExpectation
            {
                MemoryId = firstMemory.MemoryId,
                ExpectedStateHash = hashes.ComputeMemoryStateHash(firstMemory)
            },
            ReplacementCandidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = replacementCandidate.CandidateId,
                ExpectedStateHash = hashes.ComputeCandidateStateHash(replacementCandidate)
            },
            NewMemoryId = replacementMemory.MemoryId,
            ExpectedMemoryContentHash = replacementCandidate.CanonicalContentHash,
            ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(replacementMemory),
            Operation = supersedeOperation
        });

        var superseded = (await store.GetMemoryAsync(firstCandidate.TenantId, firstMemory.MemoryId))!;
        superseded.Status.Should().Be(AgentMemoryStatus.Superseded);
        var archiveOperation = CreateOperation(firstCandidate.TenantId);
        await promotion.ArchiveAsync(firstCandidate.TenantId, superseded.MemoryId, archiveOperation);

        var archiveFact = harness.Records.Single(record => record.Action?.Kind == "agent-memory.archive");
        archiveFact.Outcome!.Status.Should().Be(CrestCreates.Accountability.Abstractions.Semantics.AuditOutcomeStatuses.Succeeded);
        archiveFact.Payload!.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Curation);
        archiveFact.Payload.Data.GetProperty("previousState").GetString().Should().Be("superseded");
        archiveFact.Payload.Data.GetProperty("resultingState").GetString().Should().Be("archived");
        ((await store.GetMemoryAsync(firstCandidate.TenantId, firstMemory.MemoryId))!).Status
            .Should().Be(AgentMemoryStatus.Archived);
    }

    private static AgentMemoryCandidate CreateCandidate(
        AgentMemoryCanonicalHashProjector hashes,
        string candidateId,
        string content)
        => new()
        {
            CandidateId = candidateId,
            TenantId = AccountabilityTestFixture.FixedTenantId,
            Kind = AgentMemoryKind.Preference,
            Content = content,
            CanonicalContentHash = hashes.ComputeContentHash(
                AccountabilityTestFixture.FixedTenantId,
                Array.Empty<AgentContextSourceRef>(),
                content)
        };

    private static AgentMemoryOperationRequest CreateOperation(string tenantId)
        => new()
        {
            TenantId = tenantId,
            InvocationContext = AccountabilityTestFixture.CreateContext(tenantId),
            Reason = "archive",
            Explanation = "composition test",
            Identity = AccountabilityTestFixture.CreateIdentity()
        };

    private static AgentMemoryItem CreateMemory(
        AgentMemoryCandidate candidate,
        string memoryId,
        AgentMemoryOperationRequest operation)
        => new()
        {
            MemoryId = memoryId,
            TenantId = candidate.TenantId,
            Kind = candidate.Kind,
            Content = candidate.Content,
            CanonicalContentHash = candidate.CanonicalContentHash,
            PromotedAt = operation.Identity.OccurredAt,
            Confidence = candidate.Confidence,
            Status = AgentMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = candidate.Tags,
            DescriptorRefs = candidate.DescriptorRefs,
            SourceRefs = candidate.SourceRefs,
            RedactionKinds = candidate.RedactionKinds,
            SanitizationDiagnostics = candidate.SanitizationDiagnostics
        };

    private static AgentMemoryPromotionPlan CreatePromotionPlan(
        AgentMemoryCanonicalHashProjector hashes,
        AgentMemoryCandidate candidate,
        AgentMemoryItem memory,
        AgentMemoryOperationRequest operation)
        => new()
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
}
