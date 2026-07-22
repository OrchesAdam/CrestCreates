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
    private static AgentMemoryAccessPrincipal MakePrincipal()
        => new()
        {
            TenantId = "t1",
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

    [Fact]
    public async Task IssueAsync_RoutesThroughCoordinator()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var timeProvider = TimeProvider.System;

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockScopeProvider
            .Setup(p => p.ResolveAsync(principal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        mockCoordinator
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
                var issuedHandle = h[0] with { HandleId = "ctx-1", ExpiresAt = timeProvider.GetUtcNow().AddMinutes(10) };
                return new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult
                    {
                        Handles = [issuedHandle],
                        ReusedExisting = false
                    },
                    Grants = null,
                    CompensationToken = null,
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

        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());
        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy, timeProvider);

        var result = await issuer.IssueAsync(principal, origin, "context-handle", AgentMemoryResourceKind.Context, "test-context-1");

        result.Should().NotBeNull();
        result.HandleId.Should().NotBeNullOrEmpty();
        result.ExpiresAt.Should().BeAfter(timeProvider.GetUtcNow());

        // Verify coordinator was called
        mockCoordinator.Verify(
            c => c.PrepareAsync(
                principal, origin, scope, "context-handle", 0,
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IssueAsync_ReturnsOpaqueHandleIdAndExpiry()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var timeProvider = TimeProvider.System;

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var mockScopeProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockScopeProvider
            .Setup(p => p.ResolveAsync(principal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(),
                It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult
                {
                    Handles = [new AgentMemoryAccessResourceHandle
                    {
                        HandleId = "issued-ctx",
                        ResourceKind = AgentMemoryResourceKind.Context,
                        ResourceId = "res1",
                        Principal = principal,
                        ScopeFingerprint = "fp",
                        IssuingOperationId = origin.OperationId,
                        IssuedAt = timeProvider.GetUtcNow(),
                        ExpiresAt = timeProvider.GetUtcNow().AddMinutes(15)
                    }],
                    ReusedExisting = false
                },
                Grants = null,
                CompensationToken = null,
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
            });

        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(
            new AgentMemoryProjectionSecurityOptions());
        var issuer = new DefaultAgentMemoryContextHandleIssuer(
            mockCoordinator.Object, mockScopeProvider.Object, lifetimePolicy, timeProvider);

        var result = await issuer.IssueAsync(principal, origin, "context-handle", AgentMemoryResourceKind.Context, "test-context-1");

        result.HandleId.Should().Be("issued-ctx");
        result.ExpiresAt.Should().BeAfter(timeProvider.GetUtcNow());
    }
}
