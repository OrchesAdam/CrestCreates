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

    [Fact]
    public async Task MemoryWithNoRefsButSourceRefWithInvisibleDescriptor_IsFilteredOut()
    {
        // Memory has empty DescriptorRefs but a SourceRef whose DescriptorRef
        // is NOT in scope.VisibleDescriptorRefs. The effective closure includes
        // the SourceRef's DescriptorRefs, so the memory should be filtered out.
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: false); // scope only has ns/visible1
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // SourceRef has a descriptor NOT in scope
        var invisibleDesc = new DescriptorRef("ns", "invisible1");
        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.MemoryItem,
            TenantId = "t1",
            SourceId = "src-1",
            DescriptorRefs = new[] { invisibleDesc }
        };

        var memory = new AgentMemoryItem
        {
            MemoryId = "m-src-hidden",
            TenantId = "t1",
            Kind = AgentMemoryKind.ProjectFact,
            Content = "hidden via source ref",
            CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = Array.Empty<DescriptorRef>(), // empty direct refs
            SourceRefs = new[] { sourceRef }, // but source ref has invisible descriptor
            Confidence = AgentMemoryConfidence.High,
            Status = AgentMemoryStatus.Active
        };

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
        outcome.Result.Items.Should().BeEmpty("memory with invisible SourceRef descriptor must be filtered out");
    }

    [Fact]
    public async Task Grant_UsesEffectiveClosure_NotSourceRefOnly()
    {
        // Grant's RequiredDescriptorRefs should use the parent memory's effective closure
        // (union of memory.DescriptorRefs + all SourceRef.DescriptorRefs), not just the
        // grant's own SourceRef.DescriptorRefs.
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();

        // Scope visible descriptors
        scope = scope with
        {
            VisibleDescriptorRefs = new[] { new DescriptorRef("ns", "visible1"), new DescriptorRef("ns", "visible2") }
        };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var descMem = new DescriptorRef("ns", "visible1");   // from memory
        var descSrc = new DescriptorRef("ns", "visible2");   // from source ref
        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.MemoryItem,
            TenantId = "t1",
            SourceId = "src-eff",
            DescriptorRefs = new[] { descSrc }
        };
        var memory = MakeMemory("m-effective", new[] { descMem }) with
        {
            SourceRefs = new[] { sourceRef }
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        IReadOnlyList<AgentMemoryAccessSourceGrant>? capturedGrants = null;
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
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
            .Callback<AgentMemoryAccessPrincipal, AgentMemoryArtifactOrigin, AgentMemoryAccessScope,
                string, int, IReadOnlyList<AgentMemoryAccessResourceHandle>, IReadOnlyList<AgentMemoryAccessSourceGrant>, CancellationToken>(
                (_, _, _, _, _, _, grants, _) => capturedGrants = grants)
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                Grants = new AgentMemoryAccessGrantIssueResult { Grants = [], ReusedExisting = false },
                CompensationToken = null,
                Receipt = new AgentMemoryArtifactBatchReceipt { HandleBatch = null, GrantBatch = null }
            });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var outcome = await core.RecallAsync(principal, origin, scope, input);

        capturedGrants.Should().NotBeNull();
        capturedGrants.Should().HaveCount(1);
        var grant = capturedGrants![0];
        // The grant's RequiredDescriptorRefs should be the effective closure
        // (union of memory.DescriptorRefs + sourceRef.DescriptorRefs), not just sourceRef.DescriptorRefs
        grant.RequiredDescriptorRefs.Should().Contain(descMem);
        grant.RequiredDescriptorRefs.Should().Contain(descSrc);
        grant.RequiredDescriptorRefs.Should().HaveCount(2);
    }

    [Fact]
    public async Task GrantKey_IncludesTenantIdAndSourceKind()
    {
        // Two source refs with the same SourceId but different SourceKind (and within same tenant)
        // must not collide. Under old GrantKey format they would have the same key.
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true) with { VisibleDescriptorRefs = Array.Empty<DescriptorRef>() };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRefConv = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1",
            SourceId = "same-id",
            RangeStart = 0,
            RangeEnd = 1
        };
        var sourceRefTask = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.TaskRecord,
            TenantId = "t1",
            SourceId = "same-id",
            RangeStart = 0,
            RangeEnd = 1
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m-collision",
            TenantId = "t1",
            Kind = AgentMemoryKind.ProjectFact,
            Content = "test",
            CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = Array.Empty<DescriptorRef>(),
            SourceRefs = new[] { sourceRefConv, sourceRefTask },
            Confidence = AgentMemoryConfidence.High,
            Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        IReadOnlyList<AgentMemoryAccessSourceGrant>? capturedGrants = null;
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
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
            .Callback<AgentMemoryAccessPrincipal, AgentMemoryArtifactOrigin, AgentMemoryAccessScope,
                string, int, IReadOnlyList<AgentMemoryAccessResourceHandle>, IReadOnlyList<AgentMemoryAccessSourceGrant>, CancellationToken>(
                (_, _, _, _, _, _, grants, _) => capturedGrants = grants)
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                Grants = new AgentMemoryAccessGrantIssueResult { Grants = [], ReusedExisting = false },
                CompensationToken = null,
                Receipt = new AgentMemoryArtifactBatchReceipt { HandleBatch = null, GrantBatch = null }
            });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var outcome = await core.RecallAsync(principal, origin, scope, input);

        // Both source refs should produce grants — no collision
        capturedGrants.Should().NotBeNull();
        capturedGrants.Should().HaveCount(2, "grants with different SourceKind must not collide");
        capturedGrants.Should().Contain(g => g.SourceRef.SourceKind == AgentSourceKind.ConversationTurn);
        capturedGrants.Should().Contain(g => g.SourceRef.SourceKind == AgentSourceKind.TaskRecord);
    }

    [Fact]
    public async Task UnsupportedSourceKind_DoesNotIssueGrant()
    {
        // SourceRef with unsupported SourceKind (MetadataContextPack) must be skipped — no grant created.
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var unsupportedRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.MetadataContextPack,
            TenantId = "t1",
            SourceId = "unsupported-src"
        };
        var memory = MakeMemory("m-unsupported") with
        {
            SourceRefs = new[] { unsupportedRef }
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        IReadOnlyList<AgentMemoryAccessSourceGrant>? capturedGrants = null;
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
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
            .Callback<AgentMemoryAccessPrincipal, AgentMemoryArtifactOrigin, AgentMemoryAccessScope,
                string, int, IReadOnlyList<AgentMemoryAccessResourceHandle>, IReadOnlyList<AgentMemoryAccessSourceGrant>, CancellationToken>(
                (_, _, _, _, _, _, grants, _) => capturedGrants = grants)
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                Grants = new AgentMemoryAccessGrantIssueResult { Grants = [], ReusedExisting = false },
                CompensationToken = null,
                Receipt = new AgentMemoryArtifactBatchReceipt { HandleBatch = null, GrantBatch = null }
            });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            TimeProvider.System);

        var outcome = await core.RecallAsync(principal, origin, scope, input);

        // No grant should be created for the unsupported SourceKind
        capturedGrants.Should().NotBeNull();
        capturedGrants.Should().BeEmpty("unsupported SourceKind must not issue grants");
    }
}
