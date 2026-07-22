using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class CrossInterfaceCredentialVisibilityTests
{
    private static readonly string ScopeFp = "c53e1dcef4caa99ad1e1a241661278d78220a035a812af8466c9093f4d45dd6e";
    private static AgentMemoryAccessPrincipal MakeNewPrincipal(
        AgentMemoryCallerKind kind = AgentMemoryCallerKind.AgentTool)
        => new()
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = kind,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

    private static CanonicalHash MakeHash(string value = "hash")
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "v1",
            ArtifactKind = "test",
            Scope = "test",
            Purpose = "test",
            ContractVersion = "v1",
            CanonicalShapeVersion = "v1"
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

    private static IAgentMemoryCurrentClosureProvider MakeClosureProvider()
    {
        var closure = new AgentMemoryCurrentClosure
        {
            TenantId = "t1",
            CurrentDescriptorRefs = Array.Empty<DescriptorRef>()
        };
        var mock = new Mock<IAgentMemoryCurrentClosureProvider>();
        mock.Setup(p => p.GetCurrentClosureAsync(
                It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(closure);
        return mock.Object;
    }

    [Fact]
    public async Task NewHandle_ResolvableByNewResolver()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var principal = MakeNewPrincipal();
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h1",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res1",
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("b1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("p1")
        };

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        var resolver = new AgentMemoryAccessHandleResolver(store, TimeProvider.System, MakeClosureProvider());
        var result = await resolver.ResolveAsync("h1", AgentMemoryResourceKind.Context, principal, MakeScope());
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task McpArtifacts_InvisibleToNewResolver_WhenCallerKindDiffers()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var mcpPrincipal = MakeNewPrincipal(AgentMemoryCallerKind.Mcp);
        var agentPrincipal = MakeNewPrincipal(AgentMemoryCallerKind.AgentTool);

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "mcp1",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res1",
            Principal = mcpPrincipal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.McpInvocation,
            OriginBindingHash = MakeHash("b1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("p1")
        };

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        var resolver = new AgentMemoryAccessHandleResolver(store, TimeProvider.System, MakeClosureProvider());
        // Resolve with wrong caller should fail (full Principal equality)
        var result = await resolver.ResolveAsync("mcp1", AgentMemoryResourceKind.Context, agentPrincipal, MakeScope());
        result.Should().BeNull();
    }

    [Fact]
    public async Task SamePrincipal_DifferentBindingHash_SeparateQuota()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var principal = MakeNewPrincipal();
        var h1 = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h1", ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "r1", Principal = principal,
            ScopeFingerprint = ScopeFp, IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var bk1 = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("b1"), ArtifactPurpose = "t",
            PreparationOrdinal = 0, ArtifactPlanHash = MakeHash("p1")
        };
        var bk2 = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("b2"), ArtifactPurpose = "t",
            PreparationOrdinal = 0, ArtifactPlanHash = MakeHash("p2")
        };

        // Both should succeed under quota=1 because different binding hash
        await store.TryIssueBatchAsync(bk1, [h1], 64, 1);
        var h2 = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h2", ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "r2", Principal = principal,
            ScopeFingerprint = ScopeFp, IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        await store.TryIssueBatchAsync(bk2, [h2], 64, 1);

        var h1ret = await store.GetAsync("h1");
        var h2ret = await store.GetAsync("h2");
        h1ret.Should().NotBeNull();
        h2ret.Should().NotBeNull();
    }

    [Fact]
    public async Task SamePrincipal_SameBindingHash_Idempotent()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var principal = MakeNewPrincipal();
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h_idem", ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "r1", Principal = principal,
            ScopeFingerprint = ScopeFp, IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var bk = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("b1"), ArtifactPurpose = "t",
            PreparationOrdinal = 0, ArtifactPlanHash = MakeHash("p1")
        };

        var r1 = await store.TryIssueBatchAsync(bk, [handle], 64, 128);
        var r2 = await store.TryIssueBatchAsync(bk, [handle], 64, 128);

        r1.ReusedExisting.Should().BeFalse();
        r2.ReusedExisting.Should().BeTrue(); // Idempotent
    }

    [Fact]
    public async Task RevokeThenRead_ReturnsRevoked()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var principal = MakeNewPrincipal();
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h_revoke", ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "r1", Principal = principal,
            ScopeFingerprint = ScopeFp, IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var bk = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("b1"), ArtifactPurpose = "t",
            PreparationOrdinal = 0, ArtifactPlanHash = MakeHash("p1")
        };

        await store.TryIssueBatchAsync(bk, [handle], 64, 128);
        await store.RevokeAsync("h_revoke", AgentMemoryCallerKind.AgentTool);

        var retrieved = await store.GetAsync("h_revoke");
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Revoked);
    }

    [Fact]
    public async Task RevokeWithWrongCallerKind_NoOp()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var principal = MakeNewPrincipal();
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h_norevoke", ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "r1", Principal = principal,
            ScopeFingerprint = ScopeFp, IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var bk = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("b1"), ArtifactPurpose = "t",
            PreparationOrdinal = 0, ArtifactPlanHash = MakeHash("p1")
        };

        await store.TryIssueBatchAsync(bk, [handle], 64, 128);
        // Try to revoke with wrong caller kind — should no-op
        await store.RevokeAsync("h_norevoke", AgentMemoryCallerKind.Mcp);

        var retrieved = await store.GetAsync("h_norevoke");
        retrieved!.State.Should().Be(AgentMemorySecurityArtifactState.Active);
    }
}
