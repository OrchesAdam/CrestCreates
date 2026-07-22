using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class AgentMemoryAccessHandleStoreConcurrencyTests
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

    private static AgentMemoryAccessScope MakeScope()
        => new()
        {
            TenantId = "t1",
            VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
            AllowUnscopedMemory = false,
            MaxVisibleDescriptorRefs = 64,
            MaxRecallCount = 32,
            MaxRecallCharacters = 32_000,
            MaxExpansionCharacters = 16_000,
            MaxContextRecallCharacters = 48_000,
            MaxCompressedBlockCount = 64,
            MaxCompressedBlockCharacters = 8_000,
            MaxCandidateCount = 64,
            MaxCandidateCharacters = 8_000,
            MaxSourceRefsPerArtifact = 64,
            MaxGrantsPerResource = 64,
            MaxGrantsPerOperation = 256,
            MaxResourceHandlesPerOperation = 128,
            MaxActiveResourceHandlesPerResource = 64,
            MaxAuditFacts = 32,
            MaxTagsPerResource = 32,
            ExpansionGrantLifetime = TimeSpan.FromMinutes(10),
            ResourceHandleLifetime = TimeSpan.FromMinutes(30)
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

    private static AgentMemoryAccessResourceHandle MakeHandle(string id)
        => new()
        {
            HandleId = id,
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = id,
            Principal = MakePrincipal(),
            ScopeFingerprint = "fp1",
            IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

    [Fact]
    public async Task TryIssueBatchAsync_ConcurrentSameBatchKey_ExactlyOneBatchCreated()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey("plan-concurrent-1");
        var handle = MakeHandle("h1");

        // Launch 50 concurrent issuances with the same batch key
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => store.TryIssueBatchAsync(batchKey, [handle], 64, 128, CancellationToken.None).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // All should succeed (the API is idempotent, not null-on-duplicate)
        results.Should().AllSatisfy(r => r.Should().NotBeNull());

        // Exactly one should be the creator, rest should be idempotent reuses
        var creators = results.Where(r => r!.ReusedExisting == false).ToList();
        creators.Should().HaveCount(1);

        var reusers = results.Where(r => r!.ReusedExisting).ToList();
        reusers.Should().HaveCount(49);

        // Verify the single batch has the correct handle
        creators[0]!.Handles.Should().HaveCount(1);
        creators[0]!.Handles[0].HandleId.Should().Be("h1");

        // Retrieve handle to verify it exists exactly once
        var retrieved = await store.GetAsync("h1");
        retrieved.Should().NotBeNull();
        retrieved!.HandleId.Should().Be("h1");
    }

    [Fact]
    public async Task TryIssueBatchAsync_ConcurrentDifferentBatchKey_AllSucceed()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);

        // 50 different batch keys (vary the OriginBindingHash to get distinct identities)
        var tasks = Enumerable.Range(0, 50)
            .Select(i =>
            {
                var batchKey = MakeBatchKey($"plan-diff-{i}") with
                {
                    OriginBindingHash = MakeHash($"binding-diff-{i}")
                };
                var handle = MakeHandle($"h{i}");
                return store.TryIssueBatchAsync(batchKey, [handle], 64, 128, CancellationToken.None).AsTask();
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // All 50 should succeed as creators
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        results.Should().OnlyContain(r => r!.ReusedExisting == false);
        results.Should().HaveCount(50);

        // Verify all handles exist
        foreach (var i in Enumerable.Range(0, 50))
        {
            var retrieved = await store.GetAsync($"h{i}");
            retrieved.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task TryIssueBatchAsync_ConcurrentSameBatchKey_DoesNotBreachQuota()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);

        // Issue first batch with per-operation quota of 1
        var batchKey1 = MakeBatchKey("plan-quota-1");
        var h1 = MakeHandle("h-quota-1");
        var r1 = await store.TryIssueBatchAsync(batchKey1, [h1], 64, 1);
        r1.ReusedExisting.Should().BeFalse();

        // Second batch with same binding hash but different identity (different purpose)
        // → different identity key → no plan conflict, but shared per-operation quota
        var batchKey2 = MakeBatchKey("plan-quota-2") with
        {
            ArtifactPurpose = "quota-test" // different purpose → different identity
        };
        var h2 = MakeHandle("h-quota-2");

        // Launch concurrent attempts with the second batch key — all should see quota exhausted
        var tasks = Enumerable.Range(0, 10)
            .Select(_ =>
            {
                var act = async () => await store.TryIssueBatchAsync(batchKey2, [h2], 64, 1, CancellationToken.None);
                return act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*quota*");
            })
            .ToArray();

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task TryIssueBatchAsync_ConcurrentSameBatchKey_PlanConflictDetected()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);

        // Issue first batch to set the identity plan
        var batchKey1 = MakeBatchKey("plan-conflict-1");
        var h1 = MakeHandle("h-conflict-1");
        var r1 = await store.TryIssueBatchAsync(batchKey1, [h1], 64, 128);
        r1.ReusedExisting.Should().BeFalse();

        // Second batch with same identity (same OriginBindingHash, same purpose, same ordinal)
        // but different ArtifactPlanHash — should be plan conflict
        var batchKey2 = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-conflict-different")
        };
        var h2 = MakeHandle("h-conflict-2");

        // Launch concurrent attempts — all should detect plan conflict
        var tasks = Enumerable.Range(0, 10)
            .Select(_ =>
            {
                var act = async () => await store.TryIssueBatchAsync(batchKey2, [h2], 64, 128, CancellationToken.None);
                return act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*plan*conflict*");
            })
            .ToArray();

        await Task.WhenAll(tasks);
    }
}
