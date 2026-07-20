using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
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
            OriginBindingHash = Hash("logical"),
            ArtifactPurpose = "memory-pack",
            PreparationOrdinal = 0,
            ArtifactPlanHash = Hash("plan-a")
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

        var changedPlan = key with { ArtifactPlanHash = Hash("plan-b") };
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

    [Fact]
    public async Task HandleResolutionRejectsAHandleAfterTheVisibleScopeShrinks()
    {
        var principal = Principal();
        var descriptor = new DescriptorRef { Namespace = "ns", Id = "descriptor", Version = 1 };
        var scope = new AgentMemoryToolAccessScope { VisibleDescriptorRefs = [descriptor], AllowUnscopedMemory = false };
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "candidate", TenantId = principal.TenantId, Kind = AgentMemoryKind.ProjectFact,
            Content = "content", Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Candidate,
            DescriptorRefs = [descriptor], SourceRefs = [], CanonicalContentHash = new CanonicalHash
            {
                Value = new string('a', 64), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "agent-memory-content", Scope = "TenantVisible", Purpose = "SourceIdentity",
                ContractVersion = "memory-hash-v2", CanonicalShapeVersion = "memory-content-hash-v2"
            }
        };
        var memory = new InMemoryAgentMemoryStore();
        await memory.SaveCandidateAsync(candidate);
        var handle = new AgentMemoryResourceHandle
        {
            HandleId = "candidate-handle", ResourceKind = AgentMemoryResourceKind.Candidate,
            ResourceId = candidate.CandidateId, Principal = principal,
            ScopeFingerprint = ScopeFingerprint(principal, scope), RequiredDescriptorRefs = [descriptor],
            IsUnscoped = false, IssuingInvocationId = principal.ExecutionId,
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        var handles = new AgentMemoryResourceHandleStore();
        await handles.TryIssueBatchAsync(Key("resolver", "resolver-plan"), [handle], 2);
        var resolver = new AgentMemoryResourceHandleResolver(
            handles, new AgentMemorySourceGrantStore(), memory, new InMemoryAgentCompressedContextStore(), TimeProvider.System);

        (await resolver.ResolveAsync(handle.HandleId, AgentMemoryResourceKind.Candidate, principal, scope)).Should().NotBeNull();
        (await resolver.ResolveAsync(handle.HandleId, AgentMemoryResourceKind.Candidate, principal,
            new AgentMemoryToolAccessScope { AllowUnscopedMemory = false })).Should().BeNull();
    }

    [Fact]
    public void SourceRefComparerDoesNotMergeAdjacentRanges()
    {
        var first = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn, TenantId = "tenant", SourceId = "conversation",
            RangeStart = 0, RangeEnd = 0
        };
        var adjacent = first with { RangeStart = 1, RangeEnd = 1 };

        AgentContextSourceRefCanonicalComparer.Instance.Equals(first, adjacent).Should().BeFalse();
    }

    [Fact]
    public async Task CandidateCreateCollisionCannotResetItsLifecycle()
    {
        var store = new InMemoryAgentMemoryStore();
        var candidate = new AgentMemoryCandidate
        {
            CandidateId = "candidate-collision", TenantId = "tenant", Kind = AgentMemoryKind.ProjectFact,
            Content = "content", Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active,
            CanonicalContentHash = new CanonicalHash
            {
                Value = new string('c', 64), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "agent-memory-content", Scope = "TenantVisible", Purpose = "SourceIdentity",
                ContractVersion = "memory-hash-v2", CanonicalShapeVersion = "memory-content-hash-v2"
            }
        };
        await store.CreateCandidateAsync(candidate);
        var replacement = candidate with { Status = AgentMemoryStatus.Candidate };

        await FluentActions.Awaiting(() => store.CreateCandidateAsync(replacement).AsTask())
            .Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);
        (await store.GetCandidateAsync("tenant", candidate.CandidateId)).Should().Match<AgentMemoryCandidate>(item => item.Status == AgentMemoryStatus.Active);
    }

    [Fact]
    public async Task CompressedContextCreateCollisionIsRejected()
    {
        var store = new InMemoryAgentCompressedContextStore();
        var context = new AgentCompressedContext { ContextId = "context-collision", TenantId = "tenant", Blocks = [] };
        await store.CreateCompressedContextAsync(context);

        await FluentActions.Awaiting(() => store.CreateCompressedContextAsync(context).AsTask())
            .Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);
    }

    [Fact]
    public async Task CompressedContextBlockCollisionRejectsTheWholeBatch()
    {
        var store = new InMemoryAgentCompressedContextStore();
        var first = new AgentCompressedContext
        {
            ContextId = "context-a", TenantId = "tenant",
            Blocks = [Block("block-shared", "first")]
        };
        await store.CreateCompressedContextAsync(first);

        var duplicateWithinBatch = new AgentCompressedContext
        {
            ContextId = "context-b", TenantId = "tenant",
            Blocks = [Block("block-duplicate", "a"), Block("block-duplicate", "b")]
        };
        await FluentActions.Awaiting(() => store.CreateCompressedContextAsync(duplicateWithinBatch).AsTask())
            .Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);

        var duplicateAcrossContexts = duplicateWithinBatch with
        {
            Blocks = [Block("block-shared", "second")]
        };
        await FluentActions.Awaiting(() => store.CreateCompressedContextAsync(duplicateAcrossContexts).AsTask())
            .Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);
        (await store.GetCompressedContextAsync("tenant", "context-b")).Should().BeNull();
        (await store.GetCompressedContextBlockAsync("tenant", "block-shared"))!.Content.Should().Be("first");
    }

    [Fact]
    public async Task AbortedArtifactBatchIsNeverReturnedAsReused()
    {
        var principal = Principal();
        var handleStore = new AgentMemoryResourceHandleStore();
        var grantStore = new AgentMemorySourceGrantStore();
        var key = Key("rollback", "rollback-plan");
        var handle = Handle(principal, "rollback-handle", DateTimeOffset.UtcNow.AddMinutes(1));
        var grant = new AgentMemorySourceGrant
        {
            GrantId = "rollback-grant",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn, TenantId = principal.TenantId,
                SourceId = "conversation", RangeStart = 0, RangeEnd = 0
            },
            Principal = principal, ScopeFingerprint = "scope", IssuingInvocationId = principal.ExecutionId,
            IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        await handleStore.TryIssueBatchAsync(key, [handle], 2);
        await grantStore.TryIssueBatchAsync(key, [grant], 2);
        await handleStore.RevokeAsync(handle.HandleId);
        await grantStore.RevokeAsync(grant.GrantId);

        await FluentActions.Awaiting(() => handleStore.TryIssueBatchAsync(
                key, [handle with { HandleId = "retry-handle" }], 2).AsTask())
            .Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);
        await FluentActions.Awaiting(() => grantStore.TryIssueBatchAsync(
                key, [grant with { GrantId = "retry-grant" }], 2).AsTask())
            .Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);
    }

    private static string ScopeFingerprint(AgentMemoryToolPrincipal principal, AgentMemoryToolAccessScope scope)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"memory-scope-v2|{principal.TenantId}|{scope.AllowUnscopedMemory}|{string.Join('|', scope.VisibleDescriptorRefs
                .OrderBy(item => item.Namespace, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Version)
                .Select(item => $"{item.Namespace}:{item.Id}:{item.Version}"))}"))).ToLowerInvariant();

    private static AgentMemoryToolPrincipal Principal() => new()
    {
        TenantId = "tenant", UserId = "user", AgentId = "agent", ExecutionId = "execution"
    };

    private static AgentMemorySecurityArtifactBatchKey Key(string purpose, string plan) => new()
    {
        OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
        OriginBindingHash = Hash("logical"),
        ArtifactPurpose = purpose, PreparationOrdinal = 0, ArtifactPlanHash = Hash(plan)
    };

    private static CanonicalHash Hash(string value) => new()
    {
        Value = value.PadLeft(64, '0'), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "test", Scope = "TenantVisible", Purpose = "Test",
        ContractVersion = "test-v1", CanonicalShapeVersion = "test-v1"
    };

    private static AgentMemoryResourceHandle Handle(AgentMemoryToolPrincipal principal, string id, DateTimeOffset expiresAt) => new()
    {
        HandleId = id, ResourceKind = AgentMemoryResourceKind.Memory, ResourceId = "memory",
        Principal = principal, ScopeFingerprint = "scope", IsUnscoped = true,
        IssuingInvocationId = principal.ExecutionId, IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = expiresAt
    };

    private static AgentCompressedContextBlock Block(string id, string content) => new()
    {
        BlockId = id, TenantId = "tenant", Content = content, CanonicalContentHash = Hash(content)
    };
}
