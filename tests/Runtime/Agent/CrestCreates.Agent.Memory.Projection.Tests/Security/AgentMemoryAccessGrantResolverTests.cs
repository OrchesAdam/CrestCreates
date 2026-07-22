using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class AgentMemoryAccessGrantResolverTests
{
    private static readonly string ScopeFp = "c53e1dcef4caa99ad1e1a241661278d78220a035a812af8466c9093f4d45dd6e";

    private static readonly DescriptorRef DescAlpha = new("ns", "alpha", 1);
    private static readonly DescriptorRef DescBeta = new("ns", "beta", 1);

    private static AgentMemoryAccessPrincipal MakePrincipal(
        string userId = "u1",
        string tenantId = "t1")
        => new()
        {
            TenantId = tenantId,
            UserId = userId,
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

    private static AgentMemoryAccessScope MakeScopeWithTenant(string tenantId)
        => MakeScope() with { TenantId = tenantId };

    private static IAgentMemoryCurrentClosureProvider MakeClosureProvider(
        string tenantId = "t1",
        DescriptorRef[]? descriptorRefs = null)
    {
        var closure = new AgentMemoryCurrentClosure
        {
            TenantId = tenantId,
            CurrentDescriptorRefs = descriptorRefs ?? Array.Empty<DescriptorRef>()
        };
        var mock = new Mock<IAgentMemoryCurrentClosureProvider>();
        mock.Setup(p => p.GetCurrentClosureAsync(
                It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(closure);
        return mock.Object;
    }

    private static IAgentMemoryCurrentClosureProvider MakeNullClosureProvider()
    {
        var mock = new Mock<IAgentMemoryCurrentClosureProvider>();
        mock.Setup(p => p.GetCurrentClosureAsync(
                It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryCurrentClosure?)null);
        return mock.Object;
    }

    [Fact]
    public async Task ResolveAsync_FullPrincipalEquality_Required()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessGrantResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principalA = MakePrincipal("u1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g1",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src1"
            },
            Principal = principalA,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op1",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding1"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan1")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var principalB = MakePrincipal("u2");
        var result = await resolver.ResolveAsync("g1", principalB, MakeScope());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_SamePrincipal_Succeeds()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessGrantResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g2",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src2"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op2",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding2"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan2")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var result = await resolver.ResolveAsync("g2", principal, MakeScope());

        result.Should().NotBeNull();
        result!.GrantId.Should().Be("g2");
    }

    [Fact]
    public async Task ResolveAsync_RevokedGrant_ReturnsNull()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessGrantResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g3",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src3"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op3",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding3"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan3")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);
        await store.RevokeAsync("g3", AgentMemoryCallerKind.AgentTool);

        var result = await resolver.ResolveAsync("g3", principal, MakeScope());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ScopeFingerprintMismatch_ReturnsNull()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessGrantResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        // Grant issued with one scope fingerprint
        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g4",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src4"
            },
            Principal = principal,
            ScopeFingerprint = "old-fingerprint-value",
            IssuingOperationId = "op4",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding4"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan4")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Resolve with a different scope — fingerprint won't match
        var result = await resolver.ResolveAsync("g4", principal, MakeScope());

        result.Should().BeNull("scope fingerprint mismatch must reject the grant");
    }

    [Fact]
    public async Task ResolveAsync_SourceResourceDeleted_ReturnsNull()
    {
        // Live closure revalidation: source resource no longer exists
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var nullClosure = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, TimeProvider.System, nullClosure);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-deleted",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src-deleted"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op-del",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-del"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-del")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var result = await resolver.ResolveAsync("g-deleted", principal, MakeScope());

        result.Should().BeNull("source resource was deleted (closure provider returned null)");
    }

    [Fact]
    public async Task ResolveAsync_SourceResourceTenantChanged_ReturnsNull()
    {
        // Grant's source resource was t1 but resource now lives in t2
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var closureProvider = MakeClosureProvider(tenantId: "t2"); // resource tenant changed
        var resolver = new AgentMemoryAccessGrantResolver(store, TimeProvider.System, closureProvider);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", tenantId: "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-tenant",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src-tenant"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op-tenant",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-tenant"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-tenant")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var scope = MakeScopeWithTenant("t1");
        var result = await resolver.ResolveAsync("g-tenant", principal, scope);

        result.Should().BeNull("source resource tenant no longer matches principal tenant");
    }

    [Fact]
    public async Task ResolveAsync_UnscopedGrant_SourceGainsDescriptor_Rejects()
    {
        // Grant was issued with empty RequiredDescriptorRefs (IsUnscoped=true).
        // Source resource later gains a descriptor.
        // Resolver should reject because issued closure (empty) != current closure (non-empty).
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        // Scope that allows unscoped memory so the early check passes
        var scope = MakeScope() with { AllowUnscopedMemory = true };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-unscoped-gained",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.MemoryItem,
                TenantId = "t1",
                SourceId = "src-unscoped"
            },
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = [], // Empty at issuance
            IsUnscoped = true,
            IssuingOperationId = "op-unscoped",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(5),
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-unscoped"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-unscoped")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Source resource now has a descriptor that wasn't there at issuance time
        var closureProvider = MakeClosureProvider(
            descriptorRefs: new[] { DescAlpha });

        var resolver = new AgentMemoryAccessGrantResolver(store, TimeProvider.System, closureProvider);

        var result = await resolver.ResolveAsync("g-unscoped-gained", principal, scope);

        result.Should().BeNull("unscoped grant must be rejected when source gains a descriptor");
    }

    [Fact]
    public async Task ResolveAsync_MemoryCandidateSourceKind_Resolved()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessGrantResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-candidate",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.MemoryCandidate,
                TenantId = "t1",
                SourceId = "cand1"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op-cand",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-cand"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-cand")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var result = await resolver.ResolveAsync("g-candidate", principal, MakeScope());

        result.Should().NotBeNull("MemoryCandidate source kind must resolve to Candidate resource kind");
        result!.GrantId.Should().Be("g-candidate");
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedSourceKind_Rejected()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessGrantResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-unsupported",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.MetadataContextPack,
                TenantId = "t1",
                SourceId = "pkg1"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op-unsupported",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-unsupported"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-unsupported")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var result = await resolver.ResolveAsync("g-unsupported", principal, MakeScope());

        result.Should().BeNull("unsupported SourceKind must be rejected (fail-closed)");
    }

    [Fact]
    public async Task ResolveAsync_TaskEventSourceKind_Resolved()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessGrantResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-taskevent",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.TaskEvent,
                TenantId = "t1",
                SourceId = "task1"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            IssuingOperationId = "op-event",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-event"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-event")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var result = await resolver.ResolveAsync("g-taskevent", principal, MakeScope());

        result.Should().NotBeNull("TaskEvent source kind must resolve to AgentMemoryResourceKind.TaskEvent");
        result!.GrantId.Should().Be("g-taskevent");
    }
}
