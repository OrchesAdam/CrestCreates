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
}
