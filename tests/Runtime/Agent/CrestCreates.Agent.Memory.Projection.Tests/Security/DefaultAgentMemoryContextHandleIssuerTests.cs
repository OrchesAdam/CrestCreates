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

public class DefaultAgentMemoryContextHandleIssuerTests
{
    private static AgentMemoryAccessPrincipal MakePrincipal(string tenantId = "t1")
        => new()
        {
            TenantId = tenantId,
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

    private static AgentMemoryArtifactOrigin MakeOrigin()
        => new()
        {
            Kind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            BindingHash = new CanonicalHash
            {
                Value = "hash",
                Algorithm = "SHA-256",
                AlgorithmVersion = "v1",
                ArtifactKind = "test",
                Scope = "test",
                Purpose = "test",
                ContractVersion = "v1",
                CanonicalShapeVersion = "v1"
            },
            OperationId = "op1"
        };

    private static AgentMemoryAccessScope MakeScope(bool allowUnscoped = false)
        => new()
        {
            TenantId = "t1",
            VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
            AllowUnscopedMemory = allowUnscoped,
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

    private static AgentCompressedContext MakeContext(
        string tenantId = "t1",
        string contextId = "test-context-1",
        IReadOnlyList<DescriptorRef>? descriptorRefs = null)
    {
        descriptorRefs ??= Array.Empty<DescriptorRef>();
        var sourceRefs = descriptorRefs.Count > 0
            ? new List<AgentContextSourceRef>
            {
                new()
                {
                    SourceKind = AgentSourceKind.ConversationTurn,
                    TenantId = tenantId,
                    SourceId = "src-1",
                    DescriptorRefs = descriptorRefs
                }
            }
            : new List<AgentContextSourceRef>();

        return new AgentCompressedContext
        {
            ContextId = contextId,
            TenantId = tenantId,
            Blocks = sourceRefs.Count > 0
                ? new List<AgentCompressedContextBlock>
                {
                    new()
                    {
                        BlockId = "block-1",
                        TenantId = tenantId,
                        Content = "test content",
                        CanonicalContentHash = new CanonicalHash
                        {
                            Value = "block-hash",
                            Algorithm = "SHA-256",
                            AlgorithmVersion = "v1",
                            ArtifactKind = "block",
                            Scope = "test",
                            Purpose = "test",
                            ContractVersion = "v1",
                            CanonicalShapeVersion = "v1"
                        },
                        SourceRefs = sourceRefs
                    }
                }
                : Array.Empty<AgentCompressedContextBlock>()
        };
    }

    private static Mock<IAgentMemoryAccessArtifactCoordinator> SetupCoordinator(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        TimeProvider timeProvider,
        string expectedHandleId = "issued-ctx")
    {
        var mock = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mock
            .Setup(c => c.PrepareAsync(
                principal, origin, scope, "context-handle",
                0,
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o,
                AgentMemoryAccessScope s, string purpose, int ordinal,
                IReadOnlyList<AgentMemoryAccessResourceHandle> h,
                IReadOnlyList<AgentMemoryAccessSourceGrant> g,
                CancellationToken ct) =>
            {
                var issuedHandle = h[0] with { HandleId = expectedHandleId, ExpiresAt = timeProvider.GetUtcNow().AddMinutes(10) };
                return new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult
                    {
                        Handles = [issuedHandle],
                        ReusedExisting = false
                    },
                    Grants = null,
                    CompensationToken = new AgentMemoryArtifactCompensationToken
                    {
                        TokenId = "comp-token-1"
                    },
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt
                        {
                            BatchHash = "hash",
                            Count = 1,
                            ReusedExisting = false
                        },
                        GrantBatch = null
                    }
                };
            });
        return mock;
    }

    // ===================== Valid path =====================

    [Fact]
    public async Task IssueForCallerAsync_RoutesThroughCoordinator()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true);
        var timeProvider = TimeProvider.System;
        var context = MakeContext();

        var mockContextStore = new Mock<IAgentCompressedContextStore>();
        mockContextStore
            .Setup(s => s.GetCompressedContextAsync("t1", "test-context-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var mockCoordinator = SetupCoordinator(principal, origin, scope, timeProvider);
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockScopeProvider
            .Setup(p => p.ResolveAsync(principal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());
        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy,
            mockContextStore.Object, timeProvider);

        var result = await issuer.IssueForCallerAsync(principal, origin, "test-context-1");

        result.Should().NotBeNull();
        result.HandleId.Should().Be("issued-ctx");
        result.ExpiresAt.Should().BeAfter(timeProvider.GetUtcNow());
        result.CompensationToken.Should().NotBeNull();

        // Verify coordinator was called with Context resource kind
        mockCoordinator.Verify(
            c => c.PrepareAsync(
                principal, origin, scope, "context-handle", 0,
                It.Is<IReadOnlyList<AgentMemoryAccessResourceHandle>>(
                    h => h.Count == 1 && h[0].ResourceKind == AgentMemoryResourceKind.Context),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IssueForCallerAsync_HandleRequiredRefs_MatchesContextEffectiveClosure()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var descriptorRef = new DescriptorRef("ns1", "d1", 1);

        var context = MakeContext(descriptorRefs: new[] { descriptorRef });

        var mockContextStore = new Mock<IAgentCompressedContextStore>();
        mockContextStore
            .Setup(s => s.GetCompressedContextAsync("t1", "test-context-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var scope = MakeScope();
        scope = scope with { VisibleDescriptorRefs = new[] { descriptorRef } };

        var timeProvider = TimeProvider.System;
        var mockCoordinator = SetupCoordinator(principal, origin, scope, timeProvider);
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockScopeProvider
            .Setup(p => p.ResolveAsync(principal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());
        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy,
            mockContextStore.Object, timeProvider);

        var result = await issuer.IssueForCallerAsync(principal, origin, "test-context-1");

        result.Should().NotBeNull();
        result.HandleId.Should().Be("issued-ctx");

        // Verify the handle's RequiredDescriptorRefs match context's effective closure
        mockCoordinator.Verify(
            c => c.PrepareAsync(
                principal, origin, scope, "context-handle", 0,
                It.Is<IReadOnlyList<AgentMemoryAccessResourceHandle>>(
                    h => h.Count == 1
                        && h[0].RequiredDescriptorRefs.Count == 1
                        && h[0].RequiredDescriptorRefs[0] == descriptorRef),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ===================== Error paths =====================

    [Fact]
    public async Task IssueForCallerAsync_MissingContext_Throws()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var timeProvider = TimeProvider.System;

        var mockContextStore = new Mock<IAgentCompressedContextStore>();
        mockContextStore
            .Setup(s => s.GetCompressedContextAsync("t1", "test-context-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentCompressedContext?)null);

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());

        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy,
            mockContextStore.Object, timeProvider);

        Func<Task> act = () => issuer.IssueForCallerAsync(principal, origin, "test-context-1").AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task IssueForCallerAsync_CrossTenantContext_Throws()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var timeProvider = TimeProvider.System;
        var context = MakeContext(tenantId: "t2");

        var mockContextStore = new Mock<IAgentCompressedContextStore>();
        mockContextStore
            .Setup(s => s.GetCompressedContextAsync("t1", "test-context-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());

        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy,
            mockContextStore.Object, timeProvider);

        Func<Task> act = () => issuer.IssueForCallerAsync(principal, origin, "test-context-1").AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");
    }

    [Fact]
    public async Task IssueForCallerAsync_OutOfScopeDescriptor_Throws()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var descriptorRef = new DescriptorRef("ns1", "d1", 1);
        var context = MakeContext(descriptorRefs: new[] { descriptorRef });

        var mockContextStore = new Mock<IAgentCompressedContextStore>();
        mockContextStore
            .Setup(s => s.GetCompressedContextAsync("t1", "test-context-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        // Scope does NOT include descriptorRef
        var scope = MakeScope();
        scope = scope with { VisibleDescriptorRefs = Array.Empty<DescriptorRef>() };

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockScopeProvider
            .Setup(p => p.ResolveAsync(principal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());
        var timeProvider = TimeProvider.System;

        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy,
            mockContextStore.Object, timeProvider);

        Func<Task> act = () => issuer.IssueForCallerAsync(principal, origin, "test-context-1").AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*descriptor closure exceeds scope*");
    }

    [Fact]
    public async Task IssueForCallerAsync_UnscopedContext_AllowUnscopedFalse_Throws()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var context = MakeContext(); // No descriptor refs → unscoped

        var mockContextStore = new Mock<IAgentCompressedContextStore>();
        mockContextStore
            .Setup(s => s.GetCompressedContextAsync("t1", "test-context-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var scope = MakeScope(allowUnscoped: false);
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockScopeProvider
            .Setup(p => p.ResolveAsync(principal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());
        var timeProvider = TimeProvider.System;

        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy,
            mockContextStore.Object, timeProvider);

        Func<Task> act = () => issuer.IssueForCallerAsync(principal, origin, "test-context-1").AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unscoped context not allowed*");
    }

    [Fact]
    public async Task IssueForCallerAsync_EmptyTrustedContextId_Throws()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var timeProvider = TimeProvider.System;

        var mockContextStore = new Mock<IAgentCompressedContextStore>();
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());

        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy,
            mockContextStore.Object, timeProvider);

        Func<Task> act = () => issuer.IssueForCallerAsync(principal, origin, "  ").AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*valid identity*");
    }
}
