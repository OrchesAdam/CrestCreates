using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class AgentMemoryAccessGrantStoreConcurrencyTests
{
    private static AgentMemoryAccessPrincipal MakePrincipal(
        AgentMemoryCallerKind kind = AgentMemoryCallerKind.AgentTool)
        => new()
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = kind,
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

    private static AgentMemoryAccessArtifactBatchKey MakeBatchKey(string plan)
        => new()
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash(plan)
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
    public async Task TryIssueBatchAsync_ConcurrentSameBatchKey_ExactlyOneBatchCreated()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchKey = MakeBatchKey("plan-grant-concurrent-1");
        var grant = MakeGrant("g1");

        // Launch 50 concurrent issuances with the same batch key
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => store.TryIssueBatchAsync(batchKey, [grant], 64, 256, CancellationToken.None).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // All should succeed (the API is idempotent)
        results.Should().AllSatisfy(r => r.Should().NotBeNull());

        // Exactly one should be the creator, rest should be idempotent reuses
        var creators = results.Where(r => r!.ReusedExisting == false).ToList();
        creators.Should().HaveCount(1);

        var reusers = results.Where(r => r!.ReusedExisting).ToList();
        reusers.Should().HaveCount(49);

        // Verify the single batch has the correct grant
        creators[0]!.Grants.Should().HaveCount(1);
        creators[0]!.Grants[0].GrantId.Should().Be("g1");

        // Retrieve grant to verify it exists exactly once
        var retrieved = await store.GetAsync("g1");
        retrieved.Should().NotBeNull();
        retrieved!.GrantId.Should().Be("g1");
    }

    [Fact]
    public async Task TryIssueBatchAsync_ConcurrentDifferentBatchKey_AllSucceed()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);

        // 50 different batch keys (vary the OriginBindingHash to get distinct identities)
        var tasks = Enumerable.Range(0, 50)
            .Select(i =>
            {
                var batchKey = MakeBatchKey($"plan-grant-diff-{i}") with
                {
                    OriginBindingHash = MakeHash($"binding-grant-diff-{i}")
                };
                var grant = MakeGrant($"g{i}");
                return store.TryIssueBatchAsync(batchKey, [grant], 64, 256, CancellationToken.None).AsTask();
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // All 50 should succeed as creators
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        results.Should().OnlyContain(r => r!.ReusedExisting == false);
        results.Should().HaveCount(50);

        // Verify all grants exist
        foreach (var i in Enumerable.Range(0, 50))
        {
            var retrieved = await store.GetAsync($"g{i}");
            retrieved.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task TryIssueBatchAsync_ConcurrentSameBatchKey_DoesNotBreachQuota()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);

        // Issue first batch with per-operation quota of 1
        var batchKey1 = MakeBatchKey("plan-grant-quota-1");
        var g1 = MakeGrant("g-quota-1");
        var r1 = await store.TryIssueBatchAsync(batchKey1, [g1], 64, 1);
        r1.ReusedExisting.Should().BeFalse();

        // Second batch with same binding hash but different identity (different purpose)
        // → different identity key → no plan conflict, but shared per-operation quota
        var batchKey2 = MakeBatchKey("plan-grant-quota-2") with
        {
            ArtifactPurpose = "quota-test" // different purpose → different identity
        };
        var g2 = MakeGrant("g-quota-2");

        // Launch concurrent attempts — all should see quota exhausted
        var tasks = Enumerable.Range(0, 10)
            .Select(_ =>
            {
                var act = async () => await store.TryIssueBatchAsync(batchKey2, [g2], 64, 1, CancellationToken.None);
                return act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*quota*");
            })
            .ToArray();

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task TryIssueBatchAsync_ConcurrentSameBatchKey_PlanConflictDetected()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);

        // Issue first batch to set the identity plan
        var batchKey1 = MakeBatchKey("plan-grant-conflict-1");
        var g1 = MakeGrant("g-conflict-1");
        var r1 = await store.TryIssueBatchAsync(batchKey1, [g1], 64, 256);
        r1.ReusedExisting.Should().BeFalse();

        // Second batch with same identity but different ArtifactPlanHash — plan conflict
        var batchKey2 = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-grant-conflict-different")
        };
        var g2 = MakeGrant("g-conflict-2");

        // Launch concurrent attempts — all should detect plan conflict
        var tasks = Enumerable.Range(0, 10)
            .Select(_ =>
            {
                var act = async () => await store.TryIssueBatchAsync(batchKey2, [g2], 64, 256, CancellationToken.None);
                return act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*plan*conflict*");
            })
            .ToArray();

        await Task.WhenAll(tasks);
    }
}
