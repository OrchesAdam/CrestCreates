using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
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

    // ── P0-1 Acceptance: CompressedContextBlock Grant lifecycle ──────────

    [Fact]
    public async Task CompressedContextBlock_BlockId_IssuesGrant()
    {
        // CompressedContextBlock SourceRef uses BlockId as SourceId.
        // The Grant must be issued with the Block's own closure, not the parent Context's.
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-ccb-block",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.CompressedContextBlock,
                TenantId = "t1",
                SourceId = "block-42"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [new DescriptorRef("ns", "block-desc", 1)],
            IsUnscoped = false,
            IssuingOperationId = "op-ccb",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-ccb"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-ccb")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Verify the grant is stored and retrievable
        var stored = await store.GetAsync("g-ccb-block");
        stored.Should().NotBeNull("CompressedContextBlock grant must be stored");
        stored!.SourceRef.SourceId.Should().Be("block-42", "SourceId must be BlockId, not ContextId");
        stored.SourceRef.SourceKind.Should().Be(AgentSourceKind.CompressedContextBlock);
    }

    [Fact]
    public async Task CompressedContextBlock_GrantResolves()
    {
        // Grant for CompressedContextBlock must resolve through the GrantResolver
        // using the Block's own closure (not the parent Context's).
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");
        var blockDesc = new DescriptorRef("ns", "block-desc", 1);

        // Scope must include the block's descriptor ref in VisibleDescriptorRefs
        var scope = MakeScope() with { VisibleDescriptorRefs = [blockDesc] };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-ccb-resolve",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.CompressedContextBlock,
                TenantId = "t1",
                SourceId = "block-99"
            },
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = [blockDesc],
            IsUnscoped = false,
            IssuingOperationId = "op-ccb-resolve",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-ccb-resolve"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-ccb-resolve")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Closure provider returns the Block's closure (matching the grant's RequiredDescriptorRefs)
        var closureProvider = MakeClosureProvider(tenantId: "t1", descriptorRefs: [blockDesc]);
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-ccb-resolve", principal, scope);

        result.Should().NotBeNull("CompressedContextBlock grant must resolve when closure matches");
        result!.GrantId.Should().Be("g-ccb-resolve");
    }

    [Fact]
    public async Task CompressedContextBlock_BlockDeleted_Rejects()
    {
        // If the Block no longer exists, the closure provider returns null,
        // and the grant must be rejected.
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-ccb-deleted",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.CompressedContextBlock,
                TenantId = "t1",
                SourceId = "block-deleted"
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [],
            IsUnscoped = true,
            IssuingOperationId = "op-ccb-deleted",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-ccb-deleted"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-ccb-deleted")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Closure provider returns null → Block doesn't exist
        var closureProvider = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-ccb-deleted", principal, MakeScope());

        result.Should().BeNull("Grant for deleted CompressedContextBlock must be rejected");
    }

    // ── P0-2 Acceptance: Range Contract ──────────────────────────────────

    [Fact]
    public async Task ConversationTurn_PartialRange_Rejects()
    {
        // RangeStart present but RangeEnd missing → must reject (not silently accept)
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-conv-partial",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "conv-1",
                RangeStart = 5
                // RangeEnd missing → partial range
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [],
            IsUnscoped = true,
            IssuingOperationId = "op-conv-partial",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-conv-partial"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-conv-partial")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Closure provider returns null because SourceRange.TryResolve rejects partial range
        var closureProvider = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-conv-partial", principal, MakeScope());

        result.Should().BeNull("Partial range (only RangeStart) must be rejected");
    }

    [Fact]
    public async Task ConversationTurn_NegativeRange_Rejects()
    {
        // Negative RangeStart → must reject
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-conv-neg",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "conv-1",
                RangeStart = -1,
                RangeEnd = 3
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [],
            IsUnscoped = true,
            IssuingOperationId = "op-conv-neg",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-conv-neg"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-conv-neg")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var closureProvider = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-conv-neg", principal, MakeScope());

        result.Should().BeNull("Negative range must be rejected");
    }

    [Fact]
    public async Task ConversationTurn_OutOfBounds_Rejects()
    {
        // RangeEnd >= count → must reject (not silently truncate)
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-conv-oob",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "conv-1",
                RangeStart = 0,
                RangeEnd = 999
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [],
            IsUnscoped = true,
            IssuingOperationId = "op-conv-oob",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-conv-oob"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-conv-oob")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Closure provider returns null because SourceRange.TryResolve rejects out-of-bounds
        var closureProvider = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-conv-oob", principal, MakeScope());

        result.Should().BeNull("Out-of-bounds range must be rejected");
    }

    [Fact]
    public async Task TaskEvent_PartialRange_Rejects()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-te-partial",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.TaskEvent,
                TenantId = "t1",
                SourceId = "task-1",
                RangeEnd = 5
                // RangeStart missing → partial range
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [],
            IsUnscoped = true,
            IssuingOperationId = "op-te-partial",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-te-partial"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-te-partial")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var closureProvider = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-te-partial", principal, MakeScope());

        result.Should().BeNull("Partial range (only RangeEnd) must be rejected for TaskEvent");
    }

    [Fact]
    public async Task TaskEvent_NegativeRange_Rejects()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-te-neg",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.TaskEvent,
                TenantId = "t1",
                SourceId = "task-1",
                RangeStart = -3,
                RangeEnd = 2
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [],
            IsUnscoped = true,
            IssuingOperationId = "op-te-neg",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-te-neg"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-te-neg")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var closureProvider = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-te-neg", principal, MakeScope());

        result.Should().BeNull("Negative range must be rejected for TaskEvent");
    }

    [Fact]
    public async Task TaskEvent_OutOfBounds_Rejects()
    {
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-te-oob",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.TaskEvent,
                TenantId = "t1",
                SourceId = "task-1",
                RangeStart = 0,
                RangeEnd = 9999
            },
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = [],
            IsUnscoped = true,
            IssuingOperationId = "op-te-oob",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-te-oob"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-te-oob")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        var closureProvider = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-te-oob", principal, MakeScope());

        result.Should().BeNull("Out-of-bounds range must be rejected for TaskEvent");
    }

    // ── P0 Acceptance: ScopeBinding + ClosurePolicy orthogonality ──────────

    [Fact]
    public async Task ConversationTurn_VisibleA_ThenGainsHiddenB_ResolveRejects()
    {
        // ConversationTurn: ScopeBinding=ResourceBound, ClosurePolicy=Exact
        // Grant issued with closure=[A]. Source later gains descriptor [B].
        // Exact closure policy: issuance closure must equal current live closure.
        // [A] != [A,B] → reject.
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");
        var descA = new DescriptorRef("ns", "A", 1);

        var scope = MakeScope() with { VisibleDescriptorRefs = [descA] };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-conv-drift",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "conv-drift",
                RangeStart = 0,
                RangeEnd = 1
            },
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = [descA],   // Issued closure = [A]
            IsUnscoped = false,                 // ResourceBound → always false
            IssuingOperationId = "op-conv-drift",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-conv-drift"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-conv-drift")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Current live closure = [A, B] — source gained hidden descriptor B
        var descB = new DescriptorRef("ns", "B", 1);
        var closureProvider = MakeClosureProvider(tenantId: "t1", descriptorRefs: [descA, descB]);
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-conv-drift", principal, scope);

        result.Should().BeNull(
            "ConversationTurn with Exact ClosurePolicy must reject when current closure differs from issuance closure");
    }

    [Fact]
    public async Task TaskEvent_VisibleA_ThenGainsHiddenB_ResolveRejects()
    {
        // TaskEvent: ScopeBinding=ResourceBound, ClosurePolicy=Exact
        // Same scenario as ConversationTurn — descriptor drift must be caught.
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");
        var descA = new DescriptorRef("ns", "A", 1);

        var scope = MakeScope() with { VisibleDescriptorRefs = [descA] };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-te-drift",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.TaskEvent,
                TenantId = "t1",
                SourceId = "task-drift",
                RangeStart = 0,
                RangeEnd = 1
            },
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = [descA],   // Issued closure = [A]
            IsUnscoped = false,                 // ResourceBound → always false
            IssuingOperationId = "op-te-drift",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-te-drift"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-te-drift")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Current live closure = [A, B] — source gained hidden descriptor B
        var descB = new DescriptorRef("ns", "B", 1);
        var closureProvider = MakeClosureProvider(tenantId: "t1", descriptorRefs: [descA, descB]);
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-te-drift", principal, scope);

        result.Should().BeNull(
            "TaskEvent with Exact ClosurePolicy must reject when current closure differs from issuance closure");
    }

    [Fact]
    public async Task ConversationTurn_ExactClosure_RemainsUnchanged_ResolveSucceeds()
    {
        // ConversationTurn: ClosurePolicy=Exact
        // Issuance closure = [A], current closure still = [A] → resolve succeeds.
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");
        var descA = new DescriptorRef("ns", "A", 1);

        var scope = MakeScope() with { VisibleDescriptorRefs = [descA] };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-conv-stable",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "conv-stable",
                RangeStart = 0,
                RangeEnd = 1
            },
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = [descA],
            IsUnscoped = false,
            IssuingOperationId = "op-conv-stable",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-conv-stable"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-conv-stable")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Current closure unchanged = [A]
        var closureProvider = MakeClosureProvider(tenantId: "t1", descriptorRefs: [descA]);
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-conv-stable", principal, scope);

        result.Should().NotBeNull(
            "ConversationTurn with Exact ClosurePolicy must succeed when closure unchanged");
        result!.GrantId.Should().Be("g-conv-stable");
    }

    [Fact]
    public async Task TaskRecord_EmptyClosure_ResourceExists_ResolveSucceeds()
    {
        // TaskRecord: ScopeBinding=ResourceBound, ClosurePolicy=ExistenceOnly
        // No descriptor closure comparison — only validate resource existence + identity.
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");

        var scope = MakeScope();
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var grant = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g-tr-exist",
            SourceRef = new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.TaskRecord,
                TenantId = "t1",
                SourceId = "task-exist"
                // No range — TaskRecord is NoRange
            },
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = [],         // ExistenceOnly → always empty
            IsUnscoped = false,                  // ResourceBound → always false
            IssuingOperationId = "op-tr-exist",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-tr-exist"),
            ArtifactPurpose = "source-expand",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-tr-exist")
        };

        await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

        // Resource exists (closure provider returns a result with empty refs)
        var closureProvider = MakeClosureProvider(tenantId: "t1", descriptorRefs: []);
        var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync("g-tr-exist", principal, scope);

        result.Should().NotBeNull(
            "TaskRecord with ExistenceOnly ClosurePolicy must succeed when resource exists");
        result!.GrantId.Should().Be("g-tr-exist");
    }

    [Fact]
    public async Task ResourceBoundExact_IsUnscopedAlwaysFalse()
    {
        // Resource-bound grants (ConversationTurn, TaskEvent, TaskRecord) must always
        // have IsUnscoped=false, regardless of closure content.
        // This is the ScopeBinding contract: ResourceBound → IsUnscoped=false.
        var store = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", "t1");
        var descA = new DescriptorRef("ns", "A", 1);

        var resourceBoundKinds = new[]
        {
            AgentSourceKind.ConversationTurn,
            AgentSourceKind.TaskEvent,
            AgentSourceKind.TaskRecord
        };

        foreach (var kind in resourceBoundKinds)
        {
            var scope = kind == AgentSourceKind.TaskRecord
                ? MakeScope()
                : MakeScope() with { VisibleDescriptorRefs = [descA] };
            var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

            var grant = new AgentMemoryAccessSourceGrant
            {
                GrantId = $"g-rb-{kind}",
                SourceRef = new AgentContextSourceRef
                {
                    SourceKind = kind,
                    TenantId = "t1",
                    SourceId = $"src-{kind}",
                    RangeStart = kind == AgentSourceKind.TaskRecord ? null : 0,
                    RangeEnd = kind == AgentSourceKind.TaskRecord ? null : 1
                },
                Principal = principal,
                ScopeFingerprint = scopeFp,
                RequiredDescriptorRefs = kind == AgentSourceKind.TaskRecord ? [] : [descA],
                IsUnscoped = false,  // ResourceBound → always false
                IssuingOperationId = $"op-rb-{kind}",
                IssuedAt = timeProvider.GetUtcNow(),
                ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
            };

            var batchKey = new AgentMemoryAccessArtifactBatchKey
            {
                OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
                OriginBindingHash = MakeHash($"binding-rb-{kind}"),
                ArtifactPurpose = "source-expand",
                PreparationOrdinal = 0,
                ArtifactPlanHash = MakeHash($"plan-rb-{kind}")
            };

            await store.TryIssueBatchAsync(batchKey, [grant], 64, 256);

            var closureProvider = MakeClosureProvider(tenantId: "t1",
                descriptorRefs: kind == AgentSourceKind.TaskRecord ? [] : [descA]);
            var resolver = new AgentMemoryAccessGrantResolver(store, timeProvider, closureProvider);

            var result = await resolver.ResolveAsync($"g-rb-{kind}", principal, scope);

            result.Should().NotBeNull(
                $"ResourceBound+Exact/ExistenceOnly grant for {kind} must resolve with IsUnscoped=false");
            result!.IsUnscoped.Should().BeFalse(
                $"ResourceBound grant for {kind} must always have IsUnscoped=false");
        }
    }
}
