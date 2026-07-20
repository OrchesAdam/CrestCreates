using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tools.Tests;

public sealed class SecurityArtifactBatchTests
{
    [Fact]
    public async Task SameBatchAndPlanIsIdempotentButChangedPlanConflicts()
    {
        var store = new AgentMemoryResourceHandleStore();
        var principal = new AgentMemoryToolPrincipal
        {
            TenantId = "tenant",
            UserId = "user",
            AgentId = "agent",
            ExecutionId = "execution"
        };
        var key = new AgentMemorySecurityArtifactBatchKey
        {
            OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
            LogicalInvocationKeyHash = "logical",
            InvocationFingerprint = "invocation",
            ArtifactPurpose = "memory-pack",
            PreparationOrdinal = 0,
            ArtifactPlanHash = "plan-a"
        };
        var handle = new AgentMemoryResourceHandle
        {
            HandleId = "hnd_test",
            ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "memory_test",
            Principal = principal,
            ScopeFingerprint = "scope",
            IsUnscoped = true,
            IssuingInvocationId = "execution",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        };

        var first = await store.TryIssueBatchAsync(key, [handle], 2);
        var retry = await store.TryIssueBatchAsync(key, [handle with { HandleId = "different" }], 2);

        first.ReusedExisting.Should().BeFalse();
        retry.ReusedExisting.Should().BeTrue();
        retry.Handles.Should().ContainSingle().Which.HandleId.Should().Be("hnd_test");

        var changedPlan = key with { ArtifactPlanHash = "plan-b" };
        var action = () => store.TryIssueBatchAsync(changedPlan, [handle with { HandleId = "another" }], 2).AsTask();
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HandleExpiryAndRevocationAreSerializedWithBatchIssuance()
    {
        var store = new AgentMemoryResourceHandleStore();
        var principal = Principal();
        var expired = Handle(principal, "expired", DateTimeOffset.UtcNow.AddSeconds(-1));
        var key = Key("expiry", "plan-expiry");

        await store.TryIssueBatchAsync(key, [expired], 2);
        (await store.GetAsync(expired.HandleId)).Should().Match<AgentMemoryResourceHandle>(item =>
            item.State == AgentMemorySecurityArtifactState.Expired);

        await store.RevokeAsync(expired.HandleId);
        (await store.GetAsync(expired.HandleId)).Should().Match<AgentMemoryResourceHandle>(item =>
            item.State == AgentMemorySecurityArtifactState.Revoked);
    }

    [Fact]
    public async Task SourceGrantStoreEnforcesResourceQuotaAndRevokesExplicitly()
    {
        var store = new AgentMemorySourceGrantStore();
        var principal = Principal();
        var source = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = principal.TenantId,
            SourceId = "conversation",
            RangeStart = 0,
            RangeEnd = 0
        };
        var first = new AgentMemorySourceGrant
        {
            GrantId = "grant-1", SourceRef = source, Principal = principal,
            ScopeFingerprint = "scope", IssuingInvocationId = principal.ExecutionId,
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        await store.TryIssueBatchAsync(Key("grant-1", "grant-plan-1"), [first], 1);

        var quotaFailure = () => store.TryIssueBatchAsync(
            Key("grant-2", "grant-plan-2"),
            [first with { GrantId = "grant-2" }], 1).AsTask();
        await quotaFailure.Should().ThrowAsync<InvalidOperationException>();

        await store.RevokeAsync(first.GrantId);
        (await store.GetAsync(first.GrantId)).Should().Match<AgentMemorySourceGrant>(item =>
            item.State == AgentMemorySecurityArtifactState.Revoked);
    }

    [Fact]
    public async Task PreparedBatchRollbackMatchesArtifactIdsAndRetainsReusedArtifacts()
    {
        var store = new AgentMemorySecurityArtifactBatchStore();
        var key = Key("prepared", "prepared-plan");
        var created = new AgentMemoryPreparedSecurityArtifact
        {
            Kind = AgentMemorySecurityArtifactKind.ResourceHandle,
            ResourceKind = "Memory", ResourceId = "memory", ArtifactId = "created",
            Disposition = PreparedArtifactDisposition.CreatedByBatch
        };
        var reused = created with
        {
            ArtifactId = "reused", Disposition = PreparedArtifactDisposition.ReusedExisting
        };

        await store.PrepareAsync(key, [created, reused]);
        await store.RevokeCreatedAsync(key, [created with { }]);

        var remaining = await store.PrepareAsync(key, [reused]);
        remaining.Should().ContainSingle().Which.ArtifactId.Should().Be("reused");
    }

    private static AgentMemoryToolPrincipal Principal() => new()
    {
        TenantId = "tenant", UserId = "user", AgentId = "agent", ExecutionId = "execution"
    };

    private static AgentMemorySecurityArtifactBatchKey Key(string purpose, string plan) => new()
    {
        OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
        LogicalInvocationKeyHash = "logical", InvocationFingerprint = "invocation",
        ArtifactPurpose = purpose, PreparationOrdinal = 0, ArtifactPlanHash = plan
    };

    private static AgentMemoryResourceHandle Handle(AgentMemoryToolPrincipal principal, string id, DateTimeOffset expiresAt) => new()
    {
        HandleId = id, ResourceKind = AgentMemoryResourceKind.Memory, ResourceId = "memory",
        Principal = principal, ScopeFingerprint = "scope", IsUnscoped = true,
        IssuingInvocationId = principal.ExecutionId, IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = expiresAt
    };
}
