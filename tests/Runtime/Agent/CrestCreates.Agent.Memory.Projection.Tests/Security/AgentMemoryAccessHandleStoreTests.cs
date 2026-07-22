using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class AgentMemoryAccessHandleStoreTests
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

    private static AgentMemoryAccessArtifactBatchKey MakeBatchKey()
        => new()
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan1")
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
    public async Task TryIssueBatchAsync_IssuesNewHandles()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var handle = MakeHandle("h1");

        var result = await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);
        result.ReusedExisting.Should().BeFalse();
        result.Handles.Should().HaveCount(1);

        var retrieved = await store.GetAsync("h1");
        retrieved.Should().NotBeNull();
        retrieved!.HandleId.Should().Be("h1");
    }

    [Fact]
    public async Task TryIssueBatchAsync_Idempotent_SameBatchKeyReturnsExisting()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var handle = MakeHandle("h1");

        var r1 = await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);
        var r2 = await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        r2.ReusedExisting.Should().BeTrue();
        r2.Handles.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAsync_ActiveHandle_ReturnsActive()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var handle = MakeHandle("h1");

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);
        var retrieved = await store.GetAsync("h1");

        retrieved.Should().NotBeNull();
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Active);
    }

    [Fact]
    public async Task RevokeAsync_ExpectedCallerKindMatch_Revokes()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var handle = MakeHandle("h1");

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);
        await store.RevokeAsync("h1", AgentMemoryCallerKind.AgentTool);

        var retrieved = await store.GetAsync("h1");
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Revoked);
    }

    [Fact]
    public async Task RevokeAsync_ExpectedCallerKindMismatch_SilentReturn()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var handle = MakeHandle("h1");

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);
        await store.RevokeAsync("h1", AgentMemoryCallerKind.Mcp);

        var retrieved = await store.GetAsync("h1");
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Active);
    }

    [Fact]
    public async Task PerOperationQuota_ExceedsMax_Throws()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey1 = MakeBatchKey() with { OriginBindingHash = MakeHash("binding-a") };
        var batchKey2 = MakeBatchKey() with { OriginBindingHash = MakeHash("binding-b") };

        // Issue under max for binding-a
        var h1 = MakeHandle("h1");
        await store.TryIssueBatchAsync(batchKey1, [h1], 64, 128);

        // Different binding hash should have separate quota (no throw)
        var h2 = MakeHandle("h2");
        await store.TryIssueBatchAsync(batchKey2, [h2], 64, 128);

        // Now try to exceed per-operation quota for binding-a
        // maxActivePerOperation = 1 for the purpose of this test
        var store2 = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var ba1 = MakeBatchKey() with { OriginBindingHash = MakeHash("binding-a"), ArtifactPlanHash = MakeHash("plan-a1") };
        await store2.TryIssueBatchAsync(ba1, [MakeHandle("ha1")], 64, 1);

        // Same binding hash but different batch key (different plan hash) → quota applies
        var ba2 = MakeBatchKey() with { OriginBindingHash = MakeHash("binding-a"), ArtifactPlanHash = MakeHash("plan-a2") };
        var act = async () => await store2.TryIssueBatchAsync(ba2, [MakeHandle("ha2")], 64, 1);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SameBatch_DuplicateResource_ExceedsQuota()
    {
        // Batch with 2 handles for same resource, maxActivePerResource=1 → throws
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var h1 = MakeHandle("h1") with { ResourceId = "shared-resource" };
        var h2 = MakeHandle("h2") with { ResourceId = "shared-resource" };

        var act = async () => await store.TryIssueBatchAsync(batchKey, [h1, h2], maxActivePerResource: 1, maxActivePerOperation: 128);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SameBatch_DuplicateResource_WithinQuota()
    {
        // Batch with 2 handles for same resource, maxActivePerResource=2 → succeeds
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var batchKey = MakeBatchKey();
        var h1 = MakeHandle("h1") with { ResourceId = "shared-resource" };
        var h2 = MakeHandle("h2") with { ResourceId = "shared-resource" };

        var result = await store.TryIssueBatchAsync(batchKey, [h1, h2], maxActivePerResource: 2, maxActivePerOperation: 128);
        result.ReusedExisting.Should().BeFalse();
        result.Handles.Should().HaveCount(2);

        // Both handles should be retrievable
        var r1 = await store.GetAsync("h1");
        var r2 = await store.GetAsync("h2");
        r1.Should().NotBeNull();
        r2.Should().NotBeNull();
    }
}
