using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.ReadCore.Tests.ReadCore;

public class AgentContextReadCoreTests
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
            BindingHash = new CanonicalHash { Value = "h", Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = "test", Scope = "test", Purpose = "test", ContractVersion = "v1", CanonicalShapeVersion = "v1" },
            OperationId = "op1"
        };

    private static AgentMemoryAccessScope MakeScope()
        => new()
        {
            TenantId = "t1",
            VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
            AllowUnscopedMemory = false,
            MaxVisibleDescriptorRefs = 64,
            MaxRecallCount = 10,
            MaxRecallCharacters = 50_000,
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

    private static CanonicalHash MakeHash(string v = "abc")
        => new() { Value = v, Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = "test", Scope = "test", Purpose = "test", ContractVersion = "v1", CanonicalShapeVersion = "v1" };

    [Fact]
    public async Task RecallContextAsync_ValidInput_ReturnsOutcomeWithNullCompensationToken()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumCharacters = 1000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1",
            TenantId = "t1",
            Blocks = new[] { new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "hello", CanonicalContentHash = MakeHash() } }
        };

        var mockResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        mockResolver.Setup(r => r.ResolveAsync("ctx1", AgentMemoryResourceKind.Context, principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessResolvedResource
            {
                Handle = new AgentMemoryAccessResourceHandle
                {
                    HandleId = "ctx1", ResourceKind = AgentMemoryResourceKind.Context,
                    ResourceId = "res1", Principal = principal, ScopeFingerprint = "fp",
                    IssuingOperationId = "op1", IssuedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                }
            });

        var mockStore = new Mock<IAgentCompressedContextStore>();
        mockStore.Setup(s => s.GetCompressedContextAsync("t1", "res1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var core = new AgentContextReadCore(mockResolver.Object, mockStore.Object, TimeProvider.System);

        var outcome = await core.RecallContextAsync(principal, origin, scope, input);

        outcome.Should().NotBeNull();
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.CompensationToken.Should().BeNull(); // Read-only — no new artifacts
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecallContextAsync_ZeroOrNegativeBudget_Throws(int maximumCharacters)
    {
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumCharacters = maximumCharacters };
        var core = new AgentContextReadCore(
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            Mock.Of<IAgentCompressedContextStore>(),
            TimeProvider.System);

        var act = async () => await core.RecallContextAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Fact]
    public async Task RecallContextAsync_BudgetExceedsMax_Throws()
    {
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumCharacters = 999_999 };
        var core = new AgentContextReadCore(
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            Mock.Of<IAgentCompressedContextStore>(),
            TimeProvider.System);

        var act = async () => await core.RecallContextAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Fact]
    public async Task RecallContextAsync_HandleNotResolvable_Throws()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "nope", MaximumCharacters = 100 };

        var mockResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        mockResolver.Setup(r => r.ResolveAsync("nope", AgentMemoryResourceKind.Context, principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessResolvedResource?)null);

        var core = new AgentContextReadCore(mockResolver.Object, Mock.Of<IAgentCompressedContextStore>(), TimeProvider.System);

        var act = async () => await core.RecallContextAsync(principal, MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task RecallContextAsync_ContextNotFound_Throws()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumCharacters = 100 };

        var mockResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        mockResolver.Setup(r => r.ResolveAsync("ctx1", AgentMemoryResourceKind.Context, principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessResolvedResource
            {
                Handle = new AgentMemoryAccessResourceHandle
                {
                    HandleId = "ctx1", ResourceKind = AgentMemoryResourceKind.Context, ResourceId = "r1",
                    Principal = principal, ScopeFingerprint = "fp", IssuingOperationId = "op1",
                    IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                }
            });

        var mockStore = new Mock<IAgentCompressedContextStore>();
        mockStore.Setup(s => s.GetCompressedContextAsync("t1", "r1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentCompressedContext?)null);

        var core = new AgentContextReadCore(mockResolver.Object, mockStore.Object, TimeProvider.System);

        var act = async () => await core.RecallContextAsync(principal, MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task RecallContextAsync_Truncation_WasTruncatedTrue()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumCharacters = 3 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1",
            TenantId = "t1",
            Blocks = new[] { new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "long content here", CanonicalContentHash = MakeHash() } }
        };

        var mockResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryAccessScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessResolvedResource
            {
                Handle = new AgentMemoryAccessResourceHandle
                {
                    HandleId = "ctx1", ResourceKind = AgentMemoryResourceKind.Context, ResourceId = "r1",
                    Principal = principal, ScopeFingerprint = "fp", IssuingOperationId = "op1",
                    IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                }
            });

        var mockStore = new Mock<IAgentCompressedContextStore>();
        mockStore.Setup(s => s.GetCompressedContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var core = new AgentContextReadCore(mockResolver.Object, mockStore.Object, TimeProvider.System);

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        outcome.Result.WasTruncated.Should().BeTrue();
    }
}
