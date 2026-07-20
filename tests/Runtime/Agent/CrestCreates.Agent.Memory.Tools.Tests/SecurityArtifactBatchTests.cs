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
}
