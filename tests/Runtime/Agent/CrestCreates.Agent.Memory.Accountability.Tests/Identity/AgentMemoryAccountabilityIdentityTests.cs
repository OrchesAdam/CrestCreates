using System.Text.Json;
using CrestCreates.Agent.Memory.Accountability.CanonicalHashing;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Identity;

/// <summary>
/// Normative identity tests (spec §15.1). The AuditId is a canonical hash of the
/// tenant/action/operation/payload-kind/version shape only; OccurredAt, outcome,
/// payload data, and correlation are deliberately excluded from the identity and
/// carried by the record hash (Integrity) instead.
/// </summary>
public sealed class AgentMemoryAccountabilityIdentityTests
{
    [Fact]
    public async Task SameOperationSameFact_Should_BeStable()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);
        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);

        // A Duplicate (not a Conflict) proves the second publish resolved to the
        // same deterministic AuditId; the sink keeps only the first record.
        harness.Records.Should().HaveCount(1);
        var expectedAuditId = harness.Projector.ComputeAuditId(
            AccountabilityTestFixture.FixedTenantId, "agent-memory.recall", identity.OperationId, "agent-memory.recall.result", 1);
        harness.Records[0].AuditId.Should().Be(expectedAuditId);
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_RECORDED").Should().BeTrue();
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE").Should().BeTrue();
    }

    [Fact]
    public async Task SameOperationDifferentFact_Should_UseSameAuditId()
    {
        using var first = new AccountabilityTestFixture.ProducerHarness();
        using var second = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        var firstFact = AccountabilityTestFixture.CreateRecallPayload(returnedCount: 1);
        var secondFact = AccountabilityTestFixture.CreateRecallPayload(returnedCount: 5);

        await first.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), firstFact);
        await second.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), secondFact);

        first.Records[0].AuditId.Should().Be(second.Records[0].AuditId);
        first.Records[0].Integrity.Should().NotBe(second.Records[0].Integrity);
    }

    [Fact]
    public async Task EstablishedFactRepublish_Should_ReuseOperationIdAndOccurredAt()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);
        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);

        harness.Records.Should().HaveCount(1);
        harness.Records[0].Runtime.ExecutionId.Should().Be(identity.OperationId);
        harness.Records[0].OccurredAt.Should().Be(identity.OccurredAt);
        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE").Should().BeTrue();
    }

    [Fact]
    public async Task ChangedOccurredAtForSameOperationId_Should_Conflict()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateRecallPayload();
        var first = AccountabilityTestFixture.CreateIdentity(
            occurredAt: new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var second = AccountabilityTestFixture.CreateIdentity(
            occurredAt: new DateTimeOffset(2026, 8, 11, 12, 0, 1, TimeSpan.Zero));

        await harness.Producer.PublishRecallAsync(first, AccountabilityTestFixture.CreateContext(), payload);
        await harness.Producer.PublishRecallAsync(second, AccountabilityTestFixture.CreateContext(), payload);

        harness.HasMessage("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT").Should().BeTrue();
    }

    [Fact]
    public async Task CorrelationId_Should_Not_Be_OperationIdentity()
    {
        using var first = new AccountabilityTestFixture.ProducerHarness();
        using var second = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        await first.Producer.PublishRecallAsync(
            identity,
            AccountabilityTestFixture.CreateContext(correlationId: "correlation-a"),
            payload);
        await second.Producer.PublishRecallAsync(
            identity,
            AccountabilityTestFixture.CreateContext(correlationId: "correlation-b"),
            payload);

        first.Records[0].AuditId.Should().Be(second.Records[0].AuditId);
        first.Records[0].Integrity.Should().NotBe(second.Records[0].Integrity);
    }

    [Fact]
    public async Task OccurredAt_Should_BeExcludedFromAuditIdAndIncludedInRecordHash()
    {
        using var first = new AccountabilityTestFixture.ProducerHarness();
        using var second = new AccountabilityTestFixture.ProducerHarness();
        var payload = AccountabilityTestFixture.CreateRecallPayload();
        var firstIdentity = AccountabilityTestFixture.CreateIdentity(
            occurredAt: new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var secondIdentity = AccountabilityTestFixture.CreateIdentity(
            occurredAt: new DateTimeOffset(2026, 8, 11, 12, 0, 2, TimeSpan.Zero));

        await first.Producer.PublishRecallAsync(firstIdentity, AccountabilityTestFixture.CreateContext(), payload);
        await second.Producer.PublishRecallAsync(secondIdentity, AccountabilityTestFixture.CreateContext(), payload);

        first.Records[0].AuditId.Should().Be(second.Records[0].AuditId);
        first.Records[0].Integrity.Should().NotBe(second.Records[0].Integrity);
        first.Records[0].OccurredAt.Should().Be(firstIdentity.OccurredAt);
        second.Records[0].OccurredAt.Should().Be(secondIdentity.OccurredAt);
    }

    [Fact]
    public async Task ProducerClock_Should_Not_ReplaceOccurredAt()
    {
        var hostClock = new DateTimeOffset(2026, 8, 11, 23, 59, 59, TimeSpan.Zero);
        using var harness = new AccountabilityTestFixture.ProducerHarness(
            timeProvider: new AccountabilityTestFixture.FixedTimeProvider(hostClock));
        var identity = AccountabilityTestFixture.CreateIdentity(
            occurredAt: new DateTimeOffset(2026, 8, 11, 8, 30, 0, TimeSpan.Zero));
        var payload = AccountabilityTestFixture.CreateRecallPayload();

        await harness.Producer.PublishRecallAsync(identity, AccountabilityTestFixture.CreateContext(), payload);

        harness.Records[0].OccurredAt.Should().Be(identity.OccurredAt);
        harness.Records[0].OccurredAt.Should().NotBe(hostClock);
    }

    [Fact]
    public void OperationIdentityFactory_Should_AllocateExactlyOncePerMemoryExecution()
    {
        var fixedTime = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var factory = new DefaultAgentMemoryOperationIdentityFactory(
            new AccountabilityTestFixture.FixedTimeProvider(fixedTime));

        var first = factory.Create();
        var second = factory.Create();

        first.OperationId.Should().StartWith("op_");
        first.OperationId.Should().NotBe(second.OperationId);
        first.OccurredAt.Should().Be(fixedTime);
        second.OccurredAt.Should().Be(fixedTime);
    }

    [Fact]
    public void Tenant_Should_Isolate_AuditIdentity()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();

        var tenantA = harness.Projector.ComputeAuditId(
            "tenant-a", "agent-memory.recall", identity.OperationId, "agent-memory.recall.result", 1);
        var tenantB = harness.Projector.ComputeAuditId(
            "tenant-b", "agent-memory.recall", identity.OperationId, "agent-memory.recall.result", 1);

        tenantA.Should().StartWith("amem-v1-");
        tenantA.Should().NotBe(tenantB);
    }

    [Fact]
    public void PayloadVersion_Should_Version_AuditIdentity()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();

        var v1 = harness.Projector.ComputeAuditId(
            "tenant-a", "agent-memory.recall", identity.OperationId, "agent-memory.recall.result", 1);
        var v2 = harness.Projector.ComputeAuditId(
            "tenant-a", "agent-memory.recall", identity.OperationId, "agent-memory.recall.result", 2);

        v1.Should().NotBe(v2);
    }

    [Fact]
    public void AuditIdentity_Should_UseCanonicalHashRuntime()
    {
        using var harness = new AccountabilityTestFixture.ProducerHarness();
        var identity = AccountabilityTestFixture.CreateIdentity();
        const string actionKind = "agent-memory.recall";
        const string payloadKind = "agent-memory.recall.result";

        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = AgentMemoryAccountabilityAuditIdProjector.ArtifactKind,
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = AgentMemoryAccountabilityAuditIdProjector.AlgorithmVersion,
                ContractVersion = AgentMemoryAccountabilityAuditIdProjector.ContractVersion,
                CanonicalShapeVersion = AgentMemoryAccountabilityAuditIdProjector.CanonicalShapeVersion
            },
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("TenantId", AccountabilityTestFixture.FixedTenantId);
                writer.WriteString("ActionKind", actionKind);
                writer.WriteString("OperationId", identity.OperationId);
                writer.WriteString("PayloadKind", payloadKind);
                writer.WriteNumber("PayloadVersion", 1);
                writer.WriteEndObject();
            });

        var expectedDigest = AccountabilityTestFixture.ComputeDigest(projection);

        var actual = harness.Projector.ComputeAuditId(
            AccountabilityTestFixture.FixedTenantId, actionKind, identity.OperationId, payloadKind, 1);

        actual.Should().Be("amem-v1-" + expectedDigest);
    }
}
