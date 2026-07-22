using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class AgentMemoryAccessHandleResolverTests
{
    private static readonly string ScopeFp = "c53e1dcef4caa99ad1e1a241661278d78220a035a812af8466c9093f4d45dd6e";

    private static readonly DescriptorRef DescAlpha = new("ns", "alpha", 1);
    private static readonly DescriptorRef DescBeta = new("ns", "beta", 1);
    private static readonly DescriptorRef DescGamma = new("ns", "gamma", 1);

    private static AgentMemoryAccessPrincipal MakePrincipal(
        string userId = "u1",
        string tenantId = "t1",
        AgentMemoryCallerKind kind = AgentMemoryCallerKind.AgentTool)
        => new()
        {
            TenantId = tenantId,
            UserId = userId,
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
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryCurrentClosure?)null);
        return mock.Object;
    }

    [Fact]
    public async Task ResolveAsync_NonExistentHandle_ReturnsNull()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessHandleResolver(
            store, TimeProvider.System, MakeClosureProvider());

        var result = await resolver.ResolveAsync(
            "nonexistent", AgentMemoryResourceKind.Context,
            MakePrincipal(), MakeScope());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_FullPrincipalEquality_Required()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessHandleResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;

        // Issue handle with principal A
        var principalA = MakePrincipal("u1");
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h1",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res1",
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

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        // Resolve with principal B (different UserId)
        var principalB = MakePrincipal("u2");
        var result = await resolver.ResolveAsync(
            "h1", AgentMemoryResourceKind.Context, principalB, MakeScope());

        result.Should().BeNull(); // Full record equality fails
    }

    [Fact]
    public async Task ResolveAsync_SamePrincipal_Succeeds()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessHandleResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h2",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res2",
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

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        var result = await resolver.ResolveAsync(
            "h2", AgentMemoryResourceKind.Context, principal, MakeScope());

        result.Should().NotBeNull();
        result!.Handle.HandleId.Should().Be("h2");
    }

    [Fact]
    public async Task ResolveAsync_WrongKind_ReturnsNull()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessHandleResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal();

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h3",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res3",
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

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        // Resolve with wrong expected kind
        var result = await resolver.ResolveAsync(
            "h3", AgentMemoryResourceKind.Memory, principal, MakeScope());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RevokedHandle_ReturnsNull()
    {
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var resolver = new AgentMemoryAccessHandleResolver(
            store, TimeProvider.System, MakeClosureProvider());
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal();

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h4",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res4",
            Principal = principal,
            ScopeFingerprint = ScopeFp,
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

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);
        await store.RevokeAsync("h4", AgentMemoryCallerKind.AgentTool);

        var result = await resolver.ResolveAsync(
            "h4", AgentMemoryResourceKind.Context, principal, MakeScope());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ResourceDeleted_ReturnsNull()
    {
        // Live closure revalidation: closure provider returns null = resource deleted
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var nullClosure = MakeNullClosureProvider();
        var resolver = new AgentMemoryAccessHandleResolver(store, TimeProvider.System, nullClosure);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h-deleted",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res-deleted",
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

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        var result = await resolver.ResolveAsync(
            "h-deleted", AgentMemoryResourceKind.Context, principal, MakeScope());

        result.Should().BeNull("resource was deleted (closure provider returned null)");
    }

    [Fact]
    public async Task ResolveAsync_ResourceGainedNewDescriptors_ReturnsNull()
    {
        // Handle was issued requiring only DescAlpha, but now the resource
        // has DescAlpha+DescBeta — closure is no longer a superset
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var closureProvider = MakeClosureProvider(
            descriptorRefs: new[] { DescAlpha, DescBeta }); // resource now has both
        var resolver = new AgentMemoryAccessHandleResolver(store, TimeProvider.System, closureProvider);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h-gained",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res-gained",
            Principal = principal,
            ScopeFingerprint = ScopeFp,
            RequiredDescriptorRefs = new[] { DescAlpha }, // handle only requires DescAlpha
            IssuingOperationId = "op-gained",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-gained"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-gained")
        };

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        var result = await resolver.ResolveAsync(
            "h-gained", AgentMemoryResourceKind.Context, principal, MakeScope());

        result.Should().BeNull("resource gained descriptors not in handle's closure");
    }

    [Fact]
    public async Task ResolveAsync_ResourceTenantChanged_ReturnsNull()
    {
        // Handle issued for t1 tenant but resource now belongs to t2
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var closureProvider = MakeClosureProvider(tenantId: "t2"); // resource tenant changed
        var resolver = new AgentMemoryAccessHandleResolver(store, TimeProvider.System, closureProvider);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1", tenantId: "t1");

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h-tenant",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res-tenant",
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

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        var scope = MakeScopeWithTenant("t1");
        var result = await resolver.ResolveAsync(
            "h-tenant", AgentMemoryResourceKind.Context, principal, scope);

        result.Should().BeNull("resource tenant no longer matches principal tenant");
    }

    [Fact]
    public async Task ResolveAsync_UnscopedHandle_ResourceGainsDescriptor_Rejects()
    {
        // Handle was issued with empty RequiredDescriptorRefs (IsUnscoped=true).
        // Resource later gains a descriptor.
        // Resolver should reject because issued closure (empty) != current closure (non-empty).
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        // Scope that allows unscoped memory so the early check passes
        var scope = MakeScope() with { AllowUnscopedMemory = true };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h-unscoped-gained",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "res-unscoped",
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

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        // Resource now has a descriptor that wasn't there at issuance time
        var closureProvider = MakeClosureProvider(
            descriptorRefs: new[] { DescAlpha });

        var resolver = new AgentMemoryAccessHandleResolver(store, TimeProvider.System, closureProvider);

        var result = await resolver.ResolveAsync(
            "h-unscoped-gained", AgentMemoryResourceKind.Context, principal, scope);

        result.Should().BeNull("unscoped handle must be rejected when resource gains a descriptor");
    }

    [Fact]
    public async Task ResolveAsync_HandleWithSourceRefDescriptors_IncludesInClosure()
    {
        // Handle is issued with effective closure that merges resource refs and source refs.
        // Resolver should pass only when both are in scope and match current closure.
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        // Scope must include both descriptors for the handle to pass
        var scope = MakeScope() with
        {
            VisibleDescriptorRefs = new[] { DescAlpha, DescBeta, DescGamma }
        };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        // Effective closure: DescAlpha (resource) + DescBeta, DescGamma (from source refs)
        var effectiveClosure = new[] { DescAlpha, DescBeta, DescGamma };
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h-sourcerefs",
            ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "res-sourcerefs",
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = effectiveClosure,
            IsUnscoped = false,
            IssuingOperationId = "op-sourcerefs",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-sourcerefs"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-sourcerefs")
        };

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        // Closure provider returns same effective closure
        var closureProvider = MakeClosureProvider(
            descriptorRefs: effectiveClosure);
        var resolver = new AgentMemoryAccessHandleResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync(
            "h-sourcerefs", AgentMemoryResourceKind.Memory, principal, scope);

        result.Should().NotBeNull("handle with merged resource+source ref closure must resolve");
        result!.Handle.HandleId.Should().Be("h-sourcerefs");
    }

    [Fact]
    public async Task ResolveAsync_HandleWithSourceRefDescriptors_PartialScopeVisibility_Rejected()
    {
        // Handle requires DescAlpha + DescBeta (effective closure from resource + source refs).
        // Scope only includes DescAlpha → resolver must reject.
        var store = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var timeProvider = TimeProvider.System;
        var principal = MakePrincipal("u1");

        var scope = MakeScope() with
        {
            VisibleDescriptorRefs = new[] { DescAlpha } // Only DescAlpha, missing DescBeta
        };
        var scopeFp = AgentMemoryScopeFingerprint.Compute(scope);

        var effectiveClosure = new[] { DescAlpha, DescBeta };
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h-partial-scope",
            ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = "res-partial-scope",
            Principal = principal,
            ScopeFingerprint = scopeFp,
            RequiredDescriptorRefs = effectiveClosure,
            IsUnscoped = false,
            IssuingOperationId = "op-partial-scope",
            IssuedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().AddMinutes(30)
        };

        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OriginBindingHash = MakeHash("binding-partial-scope"),
            ArtifactPurpose = "test",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeHash("plan-partial-scope")
        };

        await store.TryIssueBatchAsync(batchKey, [handle], 64, 128);

        var closureProvider = MakeClosureProvider(
            descriptorRefs: effectiveClosure);
        var resolver = new AgentMemoryAccessHandleResolver(store, timeProvider, closureProvider);

        var result = await resolver.ResolveAsync(
            "h-partial-scope", AgentMemoryResourceKind.Memory, principal, scope);

        result.Should().BeNull("handle with DescriptorRefs not fully in scope must be rejected");
    }
}
