using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class AgentMemoryAccessGrantStoreTests
{
    private static AgentMemoryAccessPrincipal MakePrincipal()
        => new()
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

    private static CanonicalHash MakeHash(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "test",
            Scope = "test",
            Purpose = "test",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "test-shape-v1"
        };

    private static AgentMemoryAccessArtifactBatchKey MakeBatchKey()
        => new()
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan1")
        };

    private static AgentMemoryAccessSourceGrant MakeGrant(string id)
        => new()
        {
            GrantId = id,
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src1"
            },
            Principal = MakePrincipal(),
            ScopeFingerprint = "fp1",
            IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

    [Fact]
    public async Task TryIssueBatchAsync_IssuesNewGrants()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var grant = MakeGrant("g1");

        var result = await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);
        result.ReusedExisting.Should().BeFalse();
        result.Grants.Should().HaveCount(1);

        var retrieved = await store.GetAsync("g1");
        retrieved.Should().NotBeNull();
        retrieved!.GrantId.Should().Be("g1");
    }

    [Fact]
    public async Task TryIssueBatchAsync_Idempotent_SameBatchKeyReturnsExisting()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var grant = MakeGrant("g1");

        var r1 = await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);
        var r2 = await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        r2.ReusedExisting.Should().BeTrue();
        r2.Grants.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAsync_ActiveGrant_ReturnsActive()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var grant = MakeGrant("g1");

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);
        var retrieved = await store.GetAsync("g1");

        retrieved.Should().NotBeNull();
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Active);
    }

    [Fact]
    public async Task RevokeAsync_ExpectedCallerKindMatch_Revokes()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var grant = MakeGrant("g1");

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);
        await store.RevokeAsync("g1", AgentMemoryCallerKind.AgentTool);

        var retrieved = await store.GetAsync("g1");
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Revoked);
    }

    [Fact]
    public async Task RevokeAsync_ExpectedCallerKindMismatch_SilentReturn()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var grant = MakeGrant("g1");

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);
        await store.RevokeAsync("g1", AgentMemoryCallerKind.Mcp);

        var retrieved = await store.GetAsync("g1");
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Active);
    }

    [Fact]
    public async Task SameBatch_DuplicateSource_ExceedsQuota()
    {
        // Batch with 2 grants for same source, maxGrantsPerResource=1 → throws
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var g1 = MakeGrant("g1");
        var g2 = MakeGrant("g2");

        var act = async () => await store.TryIssueBatchAsync(batchKey, [g1, g2], maxActivePerResource: 1, maxActivePerOperation: 256);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SameBatch_DuplicateSource_WithinQuota()
    {
        // Batch with 2 grants for same source, maxGrantsPerResource=2 → succeeds
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var g1 = MakeGrant("g1");
        var g2 = MakeGrant("g2");

        var result = await store.TryIssueBatchAsync(batchKey, [g1, g2], maxActivePerResource: 2, maxActivePerOperation: 256);
        result.ReusedExisting.Should().BeFalse();
        result.Grants.Should().HaveCount(2);

        // Both grants should be retrievable
        var r1 = await store.GetAsync("g1");
        var r2 = await store.GetAsync("g2");
        r1.Should().NotBeNull();
        r2.Should().NotBeNull();
    }

    [Fact]
    public async Task GrantStore_ReissueExpiredBatch_CreatesFreshGrants()
    {
        var now = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now);
        var store = new AgentMemoryAccessGrantStore(timeProvider);
        var batchKey = MakeBatchKey();
        var expiredGrant = MakeGrant("g-expired") with
        {
            IssuedAt = now,
            ExpiresAt = now.AddMinutes(1)
        };

        await store.TryIssueBatchAsync(batchKey, [expiredGrant], 1, 1);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        var freshGrant = expiredGrant with
        {
            GrantId = "g-fresh",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var result = await store.TryIssueBatchAsync(batchKey, [freshGrant], 1, 1);

        result.ReusedExisting.Should().BeFalse();
        result.Grants.Should().ContainSingle()
            .Which.GrantId.Should().Be(freshGrant.GrantId);
        (await store.GetAsync(expiredGrant.GrantId)).Should().BeNull();
        (await store.GetAsync(freshGrant.GrantId))!.State
            .Should().Be(AgentMemorySecurityArtifactState.Active);
    }

    [Fact]
    public async Task ReusedBatch_NonActiveArtifact_NotReturned()
    {
        var now = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now);
        var store = new AgentMemoryAccessGrantStore(timeProvider);
        var batchKey = MakeBatchKey();
        var first = MakeGrant("g-first") with
        {
            IssuedAt = now,
            ExpiresAt = now.AddMinutes(30)
        };
        var second = MakeGrant("g-second") with
        {
            SourceRef = MakeGrant("unused").SourceRef with { SourceId = "src2" },
            IssuedAt = now,
            ExpiresAt = now.AddMinutes(30)
        };
        await store.TryIssueBatchAsync(batchKey, [first, second], 1, 2);
        await store.RevokeAsync(first.GrantId, AgentMemoryCallerKind.AgentTool);

        var replacements = new[]
        {
            first with { GrantId = "g-first-fresh" },
            second with { GrantId = "g-second-fresh" }
        };
        var result = await store.TryIssueBatchAsync(batchKey, replacements, 1, 2);

        result.ReusedExisting.Should().BeFalse();
        result.Grants.Select(grant => grant.GrantId)
            .Should().BeEquivalentTo("g-first-fresh", "g-second-fresh");
        result.Grants.Should().OnlyContain(grant =>
            grant.State == AgentMemorySecurityArtifactState.Active);
        (await store.GetAsync(second.GrantId)).Should().BeNull();
    }

    [Fact]
    public async Task ExpiredBatch_CountersAndIdentityPlanAreCleaned()
    {
        var now = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now);
        var store = new AgentMemoryAccessGrantStore(timeProvider);
        var originalKey = MakeBatchKey();
        var original = MakeGrant("g-original") with
        {
            IssuedAt = now,
            ExpiresAt = now.AddMinutes(1)
        };
        await store.TryIssueBatchAsync(originalKey, [original], 1, 1);
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var changedPlanKey = originalKey with { ArtifactPlanHash = MakeHash("plan2") };
        var replacement = original with
        {
            GrantId = "g-replacement",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var result = await store.TryIssueBatchAsync(changedPlanKey, [replacement], 1, 1);

        result.ReusedExisting.Should().BeFalse();
        result.Grants.Should().ContainSingle()
            .Which.GrantId.Should().Be(replacement.GrantId);
    }

    [Fact]
    public async Task RevokeAsync_RepeatedCall_DoesNotReleaseAnotherActiveGrantQuota()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var first = MakeGrant("g-quota-first");
        var second = MakeGrant("g-quota-second");
        await store.TryIssueBatchAsync(MakeBatchKey(), [first, second], 2, 2);

        await store.RevokeAsync(first.GrantId, AgentMemoryCallerKind.AgentTool);
        await store.RevokeAsync(first.GrantId, AgentMemoryCallerKind.AgentTool);

        var otherBatch = MakeBatchKey() with
        {
            OriginBindingHash = MakeHash("binding2"),
            ArtifactPlanHash = MakeHash("plan2")
        };
        var act = async () => await store.TryIssueBatchAsync(
            otherBatch,
            [MakeGrant("g-quota-third")],
            1,
            2);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*source grant quota*");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
