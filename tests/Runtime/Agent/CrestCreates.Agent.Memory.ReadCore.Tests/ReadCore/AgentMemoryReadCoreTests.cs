using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.ReadCore.Tests.ReadCore;

public class AgentMemoryReadCoreTests
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

    private static AgentMemoryAccessScope MakeScope(bool allowUnscoped = false)
        => new()
        {
            TenantId = "t1",
            VisibleDescriptorRefs = new[] { new DescriptorRef("ns", "visible1") },
            AllowUnscopedMemory = allowUnscoped,
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

    private static CanonicalHash MakeContentHash()
        => new()
        {
            Value = "abc",
            Algorithm = "SHA-256",
            AlgorithmVersion = "v1",
            ArtifactKind = "test",
            Scope = "test",
            Purpose = "test",
            ContractVersion = "v1",
            CanonicalShapeVersion = "v1"
        };

    private static AgentMemoryItem MakeMemory(string id, IReadOnlyList<DescriptorRef>? refs = null)
        => new()
        {
            MemoryId = id,
            TenantId = "t1",
            Kind = AgentMemoryKind.ProjectFact,
            Content = "test content",
            CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = refs ?? new[] { new DescriptorRef("ns", "visible1") },
            Confidence = AgentMemoryConfidence.High,
            Status = AgentMemoryStatus.Active
        };

    [Fact]
    public async Task RecallAsync_ValidInput_ReturnsOutcomeWithResult()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var memory = MakeMemory("m1");

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false, IsAuthoritative = true });

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator.Setup(c => c.PrepareAsync(It.IsAny<AgentMemoryAccessPrincipal>(),
                It.IsAny<AgentMemoryArtifactOrigin>(), It.IsAny<AgentMemoryAccessScope>(),
                It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                Grants = null,
                CompensationToken = new AgentMemoryArtifactCompensationToken { TokenId = "tok1" },
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "h", Count = 1, ReusedExisting = false },
                    GrantBatch = null
                }
            });

        var mockHandleResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(new AgentMemoryProjectionSecurityOptions());
        var timeProvider = TimeProvider.System;

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, mockHandleResolver.Object,
            mockCoordinator.Object, lifetimePolicy, timeProvider);

        var outcome = await core.RecallAsync(principal, origin, scope, input);

        outcome.Should().NotBeNull();
        outcome.Result.Should().NotBeNull();
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.CompensationToken.Should().NotBeNull();
    }

    [Fact]
    public async Task RecallAsync_BudgetExceedsMaxCount_Throws()
    {
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 999, CharacterBudget = 100 };
        var core = new AgentMemoryReadCore(
            Mock.Of<IAgentMemoryRetriever>(),
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            Mock.Of<IAgentMemoryAccessArtifactCoordinator>(),
            Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var act = async () => await core.RecallAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Fact]
    public async Task RecallAsync_BudgetExceedsMaxCharacters_Throws()
    {
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 1, CharacterBudget = 999_999 };
        var core = new AgentMemoryReadCore(
            Mock.Of<IAgentMemoryRetriever>(),
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            Mock.Of<IAgentMemoryAccessArtifactCoordinator>(),
            Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var act = async () => await core.RecallAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecallAsync_ZeroOrNegativeMaximumCount_Throws(int maximumCount)
    {
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = maximumCount, CharacterBudget = 100 };
        var core = new AgentMemoryReadCore(
            Mock.Of<IAgentMemoryRetriever>(),
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            Mock.Of<IAgentMemoryAccessArtifactCoordinator>(),
            Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var act = async () => await core.RecallAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecallAsync_ZeroOrNegativeCharacterBudget_Throws(int characterBudget)
    {
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = characterBudget };
        var core = new AgentMemoryReadCore(
            Mock.Of<IAgentMemoryRetriever>(),
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            Mock.Of<IAgentMemoryAccessArtifactCoordinator>(),
            Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var act = async () => await core.RecallAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Fact]
    public async Task RecallAsync_HandleNotResolvable_Throws()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 5,
            CharacterBudget = 10000,
            MemoryHandles = new[] { "bad-handle" }
        };

        var mockResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        mockResolver.Setup(r => r.ResolveAsync("bad-handle", AgentMemoryResourceKind.Memory,
                principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessResolvedResource?)null);

        var core = new AgentMemoryReadCore(
            Mock.Of<IAgentMemoryRetriever>(),
            mockResolver.Object,
            Mock.Of<IAgentMemoryAccessArtifactCoordinator>(),
            Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var act = async () => await core.RecallAsync(principal, origin, scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task RecallAsync_AllowUnscopedMemory_KeepsAllMemories()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true);
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        // Unscoped memory (no descriptor refs) — visible only when AllowUnscopedMemory=true
        var memory = MakeMemory("m1", Array.Empty<DescriptorRef>());

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator.Setup(c => c.PrepareAsync(It.IsAny<AgentMemoryAccessPrincipal>(),
                It.IsAny<AgentMemoryArtifactOrigin>(), It.IsAny<AgentMemoryAccessScope>(),
                It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                Grants = null,
                CompensationToken = null,
                Receipt = new AgentMemoryArtifactBatchReceipt { HandleBatch = null, GrantBatch = null }
            });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var outcome = await core.RecallAsync(principal, origin, scope, input);
        outcome.Result.Items.Should().HaveCount(1); // Kept even with different refs
    }

    [Fact]
    public async Task RecallAsync_CrossTenantMemory_FilteredOut()
    {
        var principal = MakePrincipal(); // TenantId = "t1"
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true);
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Memory from a different tenant — must be filtered out
        var foreignMemory = new AgentMemoryItem
        {
            MemoryId = "m-foreign",
            TenantId = "t2", // Different tenant
            Kind = AgentMemoryKind.ProjectFact,
            Content = "foreign content",
            CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = Array.Empty<DescriptorRef>(),
            Confidence = AgentMemoryConfidence.High,
            Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [foreignMemory], WasTruncated = false });

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator.Setup(c => c.PrepareAsync(It.IsAny<AgentMemoryAccessPrincipal>(),
                It.IsAny<AgentMemoryArtifactOrigin>(), It.IsAny<AgentMemoryAccessScope>(),
                It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                Grants = null,
                CompensationToken = null,
                Receipt = new AgentMemoryArtifactBatchReceipt { HandleBatch = null, GrantBatch = null }
            });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var outcome = await core.RecallAsync(principal, origin, scope, input);
        outcome.Result.Items.Should().BeEmpty("cross-tenant memory must be filtered out");
    }
}
