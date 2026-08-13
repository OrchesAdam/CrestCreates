using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.Options;
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

    private static AgentMemoryRecallOperationRequest MakeRequest(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        BuildAgentMemoryPackInput input)
        => new()
        {
            Principal = principal,
            Origin = origin,
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = $"op_{Guid.NewGuid():N}",
                OccurredAt = DateTimeOffset.UtcNow
            },
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = "t1",
                ActorId = "u1",
                ActorKind = "agent",
                CorrelationId = "correlation-test",
                InvocationId = origin.OperationId,
                InvocationSource = "agent"
            },
            Scope = scope,
            Input = input
        };

    private static AgentMemoryEffectiveResultHashProjector MakeProjector()
        => new(new DefaultCanonicalHashComputer());

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

    private static AgentMemoryAccessScope MakeScopeWithVisibleRefs(DescriptorRef[] visibleRefs, bool allowUnscoped = false)
        => new()
        {
            TenantId = "t1",
            VisibleDescriptorRefs = visibleRefs,
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
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string purpose, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
                new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult { Handles = handles.ToList(), ReusedExisting = false },
                    Grants = null,
                    CompensationToken = new AgentMemoryArtifactCompensationToken { TokenId = "tok1" },
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "h", Count = handles.Count, ReusedExisting = false },
                        GrantBatch = null
                    }
                });

        var mockHandleResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(new AgentMemoryProjectionSecurityOptions());
        var timeProvider = TimeProvider.System;

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, mockHandleResolver.Object,
            mockCoordinator.Object, lifetimePolicy, Mock.Of<IAgentMemoryCurrentClosureProvider>(), timeProvider, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

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
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(MakePrincipal(), MakeOrigin(), scope, input));
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
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(MakePrincipal(), MakeOrigin(), scope, input));
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
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(MakePrincipal(), MakeOrigin(), scope, input));
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
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(MakePrincipal(), MakeOrigin(), scope, input));
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
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(principal, origin, scope, input));
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
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string purpose, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
                new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult { Handles = handles.ToList(), ReusedExisting = false },
                    Grants = null,
                    CompensationToken = null,
                    Receipt = new AgentMemoryArtifactBatchReceipt { HandleBatch = null, GrantBatch = null }
                });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            Mock.Of<IAgentMemoryCurrentClosureProvider>(), TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
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
            Mock.Of<IAgentMemoryCurrentClosureProvider>(), TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
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
            Mock.Of<IAgentMemoryCurrentClosureProvider>(), TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        outcome.Result.Items.Should().BeEmpty("memory with invisible SourceRef descriptor must be filtered out");
    }

    [Fact]
    public async Task Grant_UsesPerSourceClosure_NotParentMemoryClosure()
    {
        // Grant's RequiredDescriptorRefs should use the individual source resource's closure
        // (resolved via IAgentMemoryCurrentClosureProvider), not the parent memory's effective closure.
        // This ensures issuance closure matches resolution closure exactly.
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

        // Mock closure provider: source resource has only descSrc in its closure
        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(
                AgentMemoryResourceKind.Memory, "t1", "src-eff",
                It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure
            {
                CurrentDescriptorRefs = new[] { descSrc },
                TenantId = "t1"
            });

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
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string op, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
                new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult { Handles = handles.ToList(), ReusedExisting = false },
                    Grants = new AgentMemoryAccessGrantIssueResult { Grants = grants.ToList(), ReusedExisting = false },
                    CompensationToken = grants.Count > 0
                        ? new AgentMemoryArtifactCompensationToken { TokenId = "comp-" + Guid.NewGuid().ToString("N") }
                        : null,
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = null,
                        GrantBatch = grants.Count > 0
                            ? new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "g1", Count = grants.Count, ReusedExisting = false }
                            : null
                    }
                });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        capturedGrants.Should().NotBeNull();
        capturedGrants.Should().HaveCount(1);
        var grant = capturedGrants![0];
        grant.RequiredDescriptorRefs.Should().Contain(descSrc);
        grant.RequiredDescriptorRefs.Should().NotContain(descMem, "grant uses source closure, not parent memory closure");
        grant.RequiredDescriptorRefs.Should().HaveCount(1);
    }

    [Fact]
    public async Task GrantKey_IncludesTenantIdAndSourceKind()
    {
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
            SourceId = "same-id"
            // No Range — TaskRecord is NoRange per RangePolicy
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
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string op, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
            {
                capturedGrants = grants;
                return new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult { Handles = handles.ToList(), ReusedExisting = false },
                    Grants = new AgentMemoryAccessGrantIssueResult { Grants = grants.ToList(), ReusedExisting = false },
                    CompensationToken = grants.Count > 0
                        ? new AgentMemoryArtifactCompensationToken { TokenId = "comp-" + Guid.NewGuid().ToString("N") }
                        : null,
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = null,
                        GrantBatch = grants.Count > 0
                            ? new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "g1", Count = grants.Count, ReusedExisting = false }
                            : null
                    }
                };
            });

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure
            {
                CurrentDescriptorRefs = Array.Empty<DescriptorRef>(),
                TenantId = "t1"
            });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

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
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string purpose, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
                new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult { Handles = handles.ToList(), ReusedExisting = false },
                    Grants = new AgentMemoryAccessGrantIssueResult { Grants = [], ReusedExisting = false },
                    CompensationToken = null,
                    Receipt = new AgentMemoryArtifactBatchReceipt { HandleBatch = null, GrantBatch = null }
                });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            Mock.Of<IAgentMemoryCurrentClosureProvider>(), TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        // No grant should be created for the unsupported SourceKind
        capturedGrants.Should().NotBeNull();
        capturedGrants.Should().BeEmpty("unsupported SourceKind must not issue grants");
    }

    // ── P0-1 acceptance: Source Grant uses per-source closure, not parent Memory closure ──

    [Fact]
    public async Task MultiSourceMemory_EachGrantUsesOwnClosure_AndResolves()
    {
        // Memory: DescriptorRefs=[A], Source1 refs=[B], Source2 refs=[C]
        // Scope must include all effective closure refs [A,B,C] for visibility
        // Each grant must use its own source's closure, not the parent memory's [A,B,C]
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var descA = new DescriptorRef("ns", "A");
        var descB = new DescriptorRef("ns", "B");
        var descC = new DescriptorRef("ns", "C");
        var scope = MakeScopeWithVisibleRefs(new[] { descA, descB, descC });
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var memory = MakeMemory("m1") with
        {
            DescriptorRefs = [descA],
            SourceRefs =
            [
                new AgentContextSourceRef { SourceKind = AgentSourceKind.ConversationTurn, TenantId = "t1", SourceId = "s1", DescriptorRefs = [descB] },
                new AgentContextSourceRef { SourceKind = AgentSourceKind.TaskRecord, TenantId = "t1", SourceId = "s2", DescriptorRefs = [descC] }
            ]
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        IReadOnlyList<AgentMemoryAccessSourceGrant>? capturedGrants = null;
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string op, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
            {
                capturedGrants = grants;
                return new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult { Handles = handles.ToList(), ReusedExisting = false },
                    Grants = new AgentMemoryAccessGrantIssueResult { Grants = grants.ToList(), ReusedExisting = false },
                    CompensationToken = grants.Count > 0
                        ? new AgentMemoryArtifactCompensationToken { TokenId = "comp-" + Guid.NewGuid().ToString("N") }
                        : null,
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = null,
                        GrantBatch = grants.Count > 0
                            ? new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "g1", Count = grants.Count, ReusedExisting = false }
                            : null
                    }
                };
            });

        // Closure provider returns per-resource closure (simulating real closure resolution)
        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(
                It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .Returns<AgentMemoryResourceKind, string, string, AgentContextSourceRef?, CancellationToken>(
                (_, _, _, sourceRef, _) => ValueTask.FromResult<AgentMemoryCurrentClosure?>(new()
                {
                    // Each source gets its own closure, NOT [A,B,C]
                    CurrentDescriptorRefs = sourceRef?.SourceKind == AgentSourceKind.ConversationTurn
                        ? new[] { descB }
                        : sourceRef?.SourceKind == AgentSourceKind.TaskRecord
                            ? new[] { descC }
                            : new[] { descA },
                    TenantId = "t1"
                }));

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        // Diagnostic: check if coordinator was called (visibility filtering may have excluded the memory)
        outcome.Should().NotBeNull("ReadCore should return a result");
        outcome.Result.Should().NotBeNull("ReadCore result should not be null");

        capturedGrants.Should().NotBeNull();
        capturedGrants.Should().HaveCount(2);
        // Grant for Source1 (ConversationTurn, ClosurePolicy=Exact) must have closure [B], not [A,B,C]
        capturedGrants.Should().Contain(g =>
            g.SourceRef.SourceKind == AgentSourceKind.ConversationTurn &&
            g.RequiredDescriptorRefs.SequenceEqual(new[] { descB }));
        // Grant for Source2 (TaskRecord, ClosurePolicy=ExistenceOnly) must have empty RequiredDescriptorRefs
        capturedGrants.Should().Contain(g =>
            g.SourceRef.SourceKind == AgentSourceKind.TaskRecord &&
            g.RequiredDescriptorRefs.Count == 0);
    }

    [Fact]
    public async Task MemoryLevelDescriptor_DoesNotPolluteSourceGrant()
    {
        // Memory: DescriptorRefs=[A], Source1 refs=[] (no descriptors)
        // Grant for Source1 must have empty closure, NOT [A] from parent memory
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var descA = new DescriptorRef("ns", "A");
        var scope = MakeScopeWithVisibleRefs(new[] { descA });
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var memory = MakeMemory("m1") with
        {
            DescriptorRefs = [descA],
            SourceRefs =
            [
                new AgentContextSourceRef { SourceKind = AgentSourceKind.ConversationTurn, TenantId = "t1", SourceId = "s1", DescriptorRefs = [] }
            ]
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        IReadOnlyList<AgentMemoryAccessSourceGrant>? capturedGrants = null;
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string op, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
            {
                capturedGrants = grants;
                return new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult { Handles = handles.ToList(), ReusedExisting = false },
                    Grants = new AgentMemoryAccessGrantIssueResult { Grants = grants.ToList(), ReusedExisting = false },
                    CompensationToken = grants.Count > 0
                        ? new AgentMemoryArtifactCompensationToken { TokenId = "comp-" + Guid.NewGuid().ToString("N") }
                        : null,
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = null,
                        GrantBatch = grants.Count > 0
                            ? new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "g1", Count = grants.Count, ReusedExisting = false }
                            : null
                    }
                };
            });

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(
                It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .Returns<AgentMemoryResourceKind, string, string, AgentContextSourceRef?, CancellationToken>(
                (_, _, _, sourceRef, _) => ValueTask.FromResult<AgentMemoryCurrentClosure?>(new()
                {
                    CurrentDescriptorRefs = sourceRef?.DescriptorRefs ?? new[] { descA },
                    TenantId = "t1"
                }));

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        capturedGrants.Should().NotBeNull();
        capturedGrants.Should().HaveCount(1);
        // Source1's grant must have empty closure — memory-level [A] must NOT pollute it
        capturedGrants[0].RequiredDescriptorRefs.Should().BeEmpty(
            "source grant must use its own source closure, not parent memory's descriptor");
    }

    [Fact]
    public async Task ConversationTurn_EmptyClosure_AllowUnscopedFalse_PreparesAndResolves()
    {
        // Resource-bound Grant: empty source closure + IsUnscoped=false must work even when AllowUnscopedMemory=false
        // The memory itself must have a visible descriptor to pass visibility filtering
        var descA = new DescriptorRef { Id = "desc-a", Version = 1 };
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: false) with { VisibleDescriptorRefs = new[] { descA } };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1",
            SourceId = "conv-1",
            RangeStart = 0,
            RangeEnd = 1
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },  // Memory has visible descriptor
            SourceRefs = new[] { sourceRef },   // But source has empty closure
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        grantCapture.Grants.Should().NotBeNull();
        grantCapture.Grants.Should().ContainSingle(g =>
            g.RequiredDescriptorRefs.Count == 0 && g.IsUnscoped == false,
            "ConversationTurn is ResourceBound: empty closure + IsUnscoped=false must be accepted even when AllowUnscopedMemory=false");
    }

    [Fact]
    public async Task TaskRecord_EmptyClosure_AllowUnscopedFalse_PreparesAndResolves()
    {
        var descA = new DescriptorRef { Id = "desc-a", Version = 1 };
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: false) with { VisibleDescriptorRefs = new[] { descA } };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.TaskRecord,
            TenantId = "t1",
            SourceId = "task-1"
            // No Range — TaskRecord is NoRange
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[] { sourceRef },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        grantCapture.Grants.Should().NotBeNull();
        grantCapture.Grants.Should().ContainSingle(g =>
            g.RequiredDescriptorRefs.Count == 0 && g.IsUnscoped == false,
            "TaskRecord is ResourceBound: empty closure + IsUnscoped=false must be accepted even when AllowUnscopedMemory=false");
    }

    [Fact]
    public async Task TaskEvent_EmptyClosure_AllowUnscopedFalse_PreparesAndResolves()
    {
        var descA = new DescriptorRef { Id = "desc-a", Version = 1 };
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: false) with { VisibleDescriptorRefs = new[] { descA } };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.TaskEvent,
            TenantId = "t1",
            SourceId = "task-1",
            RangeStart = 0,
            RangeEnd = 1
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[] { sourceRef },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        grantCapture.Grants.Should().NotBeNull();
        grantCapture.Grants.Should().ContainSingle(g =>
            g.RequiredDescriptorRefs.Count == 0 && g.IsUnscoped == false,
            "TaskEvent is ResourceBound: empty closure + IsUnscoped=false must be accepted even when AllowUnscopedMemory=false");
    }

    [Fact]
    public async Task DescriptorBoundGrant_EmptyClosure_AllowUnscopedFalse_Rejects()
    {
        // Descriptor-bound Grant with empty closure: IsUnscoped=true, must be rejected when AllowUnscopedMemory=false
        var descA = new DescriptorRef { Id = "desc-a", Version = 1 };
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: false) with { VisibleDescriptorRefs = new[] { descA } };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.MemoryItem,
            TenantId = "t1",
            SourceId = "mem-1"
            // No Range — MemoryItem is NoRange
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[] { sourceRef },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        // Override: Coordinator rejects IsUnscoped=true when AllowUnscopedMemory=false
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Descriptor-bound grant with IsUnscoped=true rejected when AllowUnscopedMemory=false"));

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        // Coordinator rejection propagates — Descriptor-bound grant with empty closure + AllowUnscopedMemory=false is invalid
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.RecallAsync(MakeRequest(principal, origin, scope, input)).AsTask());
    }

    [Fact]
    public async Task TaskRecord_WithRange_DoesNotIssueGrant()
    {
        // TaskRecord is NoRange — any Range in SourceRef must prevent Grant issuance
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true) with { VisibleDescriptorRefs = Array.Empty<DescriptorRef>() };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.TaskRecord,
            TenantId = "t1",
            SourceId = "task-1",
            RangeStart = 0,  // Invalid: TaskRecord is NoRange
            RangeEnd = 2
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = Array.Empty<DescriptorRef>(),
            SourceRefs = new[] { sourceRef },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        grantCapture.Grants.Should().BeNullOrEmpty("TaskRecord with Range must not issue Grant");
    }

    [Fact]
    public async Task CompressedContextBlock_WithRange_DoesNotIssueGrant()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true) with { VisibleDescriptorRefs = Array.Empty<DescriptorRef>() };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.CompressedContextBlock,
            TenantId = "t1",
            SourceId = "block-1",
            RangeStart = 0,  // Invalid: CompressedContextBlock is NoRange
            RangeEnd = 5
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = Array.Empty<DescriptorRef>(),
            SourceRefs = new[] { sourceRef },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        grantCapture.Grants.Should().BeNullOrEmpty("CompressedContextBlock with Range must not issue Grant");
    }

    [Fact]
    public async Task MemoryItem_WithRange_DoesNotIssueGrant()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true) with { VisibleDescriptorRefs = Array.Empty<DescriptorRef>() };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.MemoryItem,
            TenantId = "t1",
            SourceId = "mem-1",
            RangeStart = 0,  // Invalid: MemoryItem is NoRange
            RangeEnd = 3
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = Array.Empty<DescriptorRef>(),
            SourceRefs = new[] { sourceRef },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        grantCapture.Grants.Should().BeNullOrEmpty("MemoryItem with Range must not issue Grant");
    }

    [Fact]
    public async Task MemoryCandidate_WithRange_DoesNotIssueGrant()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true) with { VisibleDescriptorRefs = Array.Empty<DescriptorRef>() };
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.MemoryCandidate,
            TenantId = "t1",
            SourceId = "cand-1",
            RangeStart = 1,  // Invalid: MemoryCandidate is NoRange
            RangeEnd = 2
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "test", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = Array.Empty<DescriptorRef>(),
            SourceRefs = new[] { sourceRef },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);

        var mockClosureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosureProvider
            .Setup(p => p.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), "t1", It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = Array.Empty<DescriptorRef>(), TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosureProvider.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        grantCapture.Grants.Should().BeNullOrEmpty("MemoryCandidate with Range must not issue Grant");
    }

    private sealed class GrantCapture
    {
        public IReadOnlyList<AgentMemoryAccessSourceGrant>? Grants { get; set; }
    }

    private Mock<IAgentMemoryAccessArtifactCoordinator> MakeCoordinatorCapturingGrants(GrantCapture capture)
    {
        var mock = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mock
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string op, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants, CancellationToken ct) =>
            {
                capture.Grants = grants;
                return new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = handles.Count > 0
                        ? new AgentMemoryAccessHandleIssueResult
                        {
                            Handles = handles.ToList(),
                            ReusedExisting = false
                        }
                        : new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                    Grants = grants.Count > 0
                        ? new AgentMemoryAccessGrantIssueResult
                        {
                            Grants = grants.ToList(),
                            ReusedExisting = false
                        }
                        : new AgentMemoryAccessGrantIssueResult { Grants = [], ReusedExisting = false },
                    CompensationToken = grants.Count > 0
                        ? new AgentMemoryArtifactCompensationToken { TokenId = "comp-" + Guid.NewGuid().ToString("N") }
                        : null,
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = handles.Count > 0
                            ? new AgentMemoryArtifactBatchReceipt.BatchReceipt
                            {
                                BatchHash = "batch-h1", Count = handles.Count, ReusedExisting = false
                            }
                            : null,
                        GrantBatch = grants.Count > 0
                            ? new AgentMemoryArtifactBatchReceipt.BatchReceipt
                            {
                                BatchHash = "batch-g1", Count = grants.Count, ReusedExisting = false
                            }
                            : null
                    }
                };
            });
        return mock;
    }

    [Fact]
    public async Task RecallAsync_ForeignTenantPack_RejectsEntirePack()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var memory = MakeMemory("m1");
        memory = memory with { TenantId = "t1" };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "foreign-tenant", Memories = [memory], WasTruncated = false, IsAuthoritative = true });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            Mock.Of<IAgentMemoryAccessArtifactCoordinator>(), Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            Mock.Of<IAgentMemoryCurrentClosureProvider>(), TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("tenant-boundary");
    }

    [Fact]
    public async Task RecallAsync_DuplicateMemoryId_RejectsBeforePrepare()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var descA = new DescriptorRef("ns", "visible1");
        var memory1 = MakeMemory("same-id", [descA]);
        var memory2 = MakeMemory("same-id", [descA]);

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory1, memory2], WasTruncated = false, IsAuthoritative = true });

        var revoked = false;
        var token = new AgentMemoryArtifactCompensationToken { TokenId = "revoke-tok" };
        var handle1 = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h1", ResourceId = "same-id", ResourceKind = AgentMemoryResourceKind.Memory,
            Principal = principal, ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            RequiredDescriptorRefs = [descA], IsUnscoped = false,
            IssuingOperationId = "op1", IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        var handle2 = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h2", ResourceId = "same-id", ResourceKind = AgentMemoryResourceKind.Memory,
            Principal = principal, ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            RequiredDescriptorRefs = [descA], IsUnscoped = false,
            IssuingOperationId = "op1", IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [handle1, handle2], ReusedExisting = false },
                Grants = null,
                CompensationToken = token,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "h", Count = 2, ReusedExisting = false },
                    GrantBatch = null
                }
            });
        mockCoordinator
            .Setup(c => c.RevokeCreatedAsync(token, It.IsAny<CancellationToken>()))
            .Callback(() => revoked = true)
            .Returns(ValueTask.CompletedTask);

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            Mock.Of<IAgentMemoryCurrentClosureProvider>(), TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        var exception = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        exception.And.Code.Should().Be("handle-contract");
        revoked.Should().BeFalse("the invalid handle plan is rejected before any artifact is prepared");
    }

    [Fact]
    public async Task RecallAsync_DuplicateConfirmedSourceKey_ThrowsAndCompensates()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var descA = new DescriptorRef("ns", "visible1", Version: 1);
        var sourceRef = new AgentContextSourceRef { SourceKind = AgentSourceKind.MemoryItem, SourceId = "s1", TenantId = "t1" };
        var memory = MakeMemory("m1", [descA]) with { SourceRefs = [sourceRef] };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false, IsAuthoritative = true });

        var revoked = false;
        var token = new AgentMemoryArtifactCompensationToken { TokenId = "revoke-tok" };
        var grant1 = new AgentMemoryAccessSourceGrant
        {
            GrantId = "g1",
            SourceRef = sourceRef,
            Principal = principal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            RequiredDescriptorRefs = [descA],
            IsUnscoped = false,
            IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        var grant2 = grant1 with { GrantId = "g2" };
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [], ReusedExisting = false },
                Grants = new AgentMemoryAccessGrantIssueResult { Grants = [grant1, grant2], ReusedExisting = false },
                CompensationToken = token,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "h", Count = 2, ReusedExisting = false }
                }
            });
        mockCoordinator
            .Setup(c => c.RevokeCreatedAsync(token, It.IsAny<CancellationToken>()))
            .Callback(() => revoked = true)
            .Returns(ValueTask.CompletedTask);

        var mockClosure = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosure.Setup(c => c.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { CurrentDescriptorRefs = [descA], TenantId = "t1" });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosure.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var act = async () => await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        revoked.Should().BeTrue();
    }

    [Fact]
    public async Task RecallAsync_ReusedArtifacts_MappingFails_ReusedArtifactsRemainActive()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var descA = new DescriptorRef("ns", "visible1");
        var memory = MakeMemory("m1", [descA]);

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false, IsAuthoritative = true });

        var token = new AgentMemoryArtifactCompensationToken { TokenId = "revoke-tok" };
        var revoked = false;
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "h1", ResourceId = "m1", ResourceKind = AgentMemoryResourceKind.Memory,
            Principal = principal, ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            RequiredDescriptorRefs = [descA], IsUnscoped = false,
            IssuingOperationId = "op1", IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = new AgentMemoryAccessHandleIssueResult { Handles = [handle], ReusedExisting = true },
                Grants = null,
                CompensationToken = token,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt { BatchHash = "h", Count = 1, ReusedExisting = true },
                    GrantBatch = null
                }
            });
        mockCoordinator
            .Setup(c => c.RevokeCreatedAsync(token, It.IsAny<CancellationToken>()))
            .Callback(() => revoked = true)
            .Returns(ValueTask.CompletedTask);

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            Mock.Of<IAgentMemoryCurrentClosureProvider>(), TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        revoked.Should().BeFalse();
    }

    [Fact]
    public async Task RecallAsync_SameSourceAcrossItems_Succeeds()
    {
        var principal = MakePrincipal();
        var descA = new DescriptorRef("ns", "A", 1);
        var scope = MakeScopeWithVisibleRefs(new[] { descA });
        var sharedSourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1", SourceId = "conv-1",
            DescriptorRefs = new[] { descA }
        };

        var pack = new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[]
            {
                MakeMemory("m1", new[] { descA }) with { SourceRefs = new[] { sharedSourceRef } },
                MakeMemory("m2", new[] { descA }) with { SourceRefs = new[] { sharedSourceRef } }
            }
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pack);

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);
        var mockClosure = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosure.Setup(c => c.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { TenantId = "t1", CurrentDescriptorRefs = new[] { descA } });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosure.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var input = new BuildAgentMemoryPackInput { MaximumCount = 10, CharacterBudget = 10_000 };
        var outcome = await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
    }

    [Fact]
    public async Task RecallAsync_SameSourceAcrossItems_CoordinatorReceivesOneGrant()
    {
        var principal = MakePrincipal();
        var descA = new DescriptorRef("ns", "A", 1);
        var scope = MakeScopeWithVisibleRefs(new[] { descA });
        var sharedSourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1", SourceId = "conv-1",
            DescriptorRefs = new[] { descA }
        };

        var pack = new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[]
            {
                MakeMemory("m1", new[] { descA }) with { SourceRefs = new[] { sharedSourceRef } },
                MakeMemory("m2", new[] { descA }) with { SourceRefs = new[] { sharedSourceRef } }
            }
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pack);

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);
        var mockClosure = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosure.Setup(c => c.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { TenantId = "t1", CurrentDescriptorRefs = new[] { descA } });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosure.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var input = new BuildAgentMemoryPackInput { MaximumCount = 10, CharacterBudget = 10_000 };
        await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        grantCapture.Grants.Should().HaveCount(1, "same SourceKey across two items must produce exactly one Grant");
    }

    [Fact]
    public async Task RecallAsync_SameSourceAcrossItems_BothItemsReuseGrant()
    {
        var principal = MakePrincipal();
        var descA = new DescriptorRef("ns", "A", 1);
        var scope = MakeScopeWithVisibleRefs(new[] { descA });
        var sharedSourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1", SourceId = "conv-1",
            DescriptorRefs = new[] { descA }
        };

        var pack = new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[]
            {
                MakeMemory("m1", new[] { descA }) with { SourceRefs = new[] { sharedSourceRef } },
                MakeMemory("m2", new[] { descA }) with { SourceRefs = new[] { sharedSourceRef } }
            }
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pack);

        var grantCapture = new GrantCapture();
        var mockCoordinator = MakeCoordinatorCapturingGrants(grantCapture);
        var mockClosure = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosure.Setup(c => c.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure { TenantId = "t1", CurrentDescriptorRefs = new[] { descA } });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosure.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var input = new BuildAgentMemoryPackInput { MaximumCount = 10, CharacterBudget = 10_000 };
        var outcome = await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        outcome.Result.Items.Should().HaveCount(2);
        var grantId1 = outcome.Result.Items[0].SourceGrants.FirstOrDefault()?.GrantId;
        var grantId2 = outcome.Result.Items[1].SourceGrants.FirstOrDefault()?.GrantId;
        grantId1.Should().NotBeNullOrEmpty();
        grantId1.Should().Be(grantId2, "both items must share the same deduplicated Grant");
    }

    [Fact]
    public async Task RecallAsync_ConflictingDescriptorRefsForSameSourceKey_RejectsBeforePrepare()
    {
        var principal = MakePrincipal();
        var descA = new DescriptorRef("ns", "A", 1);
        var descB = new DescriptorRef("ns", "B", 1);
        var scope = MakeScopeWithVisibleRefs(new[] { descA, descB });
        var sourceRef1 = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1", SourceId = "conv-1",
            DescriptorRefs = new[] { descA }
        };
        var sourceRef2 = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1", SourceId = "conv-1",
            DescriptorRefs = new[] { descB }
        };

        var pack = new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[]
            {
                MakeMemory("m1", new[] { descA }) with { SourceRefs = new[] { sourceRef1 } },
                MakeMemory("m2", new[] { descB }) with { SourceRefs = new[] { sourceRef2 } }
            }
        };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pack);

        var coordinatorCalled = false;
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator.Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => coordinatorCalled = true)
            .ReturnsAsync(new AgentMemoryAccessPreparedArtifacts
            {
                Handles = null,
                Grants = new AgentMemoryAccessGrantIssueResult { Grants = [], ReusedExisting = false },
                CompensationToken = null,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = null
                }
            });

        var mockClosure = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosure.Setup(c => c.GetCurrentClosureAsync(It.IsAny<AgentMemoryResourceKind>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentContextSourceRef?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryResourceKind kind, string tenantId, string resourceId,
                AgentContextSourceRef? sourceRef, CancellationToken ct) =>
                new AgentMemoryCurrentClosure
                {
                    TenantId = tenantId,
                    CurrentDescriptorRefs = sourceRef?.DescriptorRefs ?? Array.Empty<DescriptorRef>()
                });

        var core = new AgentMemoryReadCore(
            mockRetriever.Object, Mock.Of<IAgentMemoryAccessHandleResolver>(),
            mockCoordinator.Object, Mock.Of<IAgentMemoryArtifactLifetimePolicy>(),
            mockClosure.Object, TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var input = new BuildAgentMemoryPackInput { MaximumCount = 10, CharacterBudget = 10_000 };
        var act = async () => await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        coordinatorCalled.Should().BeFalse("conflicting descriptor refs must reject before Coordinator is called");
    }

    [Fact]
    public async Task MemoryRecall_MissingConfirmedHandle_ThrowsAndCompensates()
    {
        var scenario = await RunHandleContractScenarioAsync(_ => []);

        scenario.Exception.Should().NotBeNull();
        scenario.Exception!.Code.Should().Be("handle-contract");
        scenario.Revoked.Should().BeTrue();
        scenario.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task MemoryRecall_ExtraConfirmedHandle_ThrowsAndCompensates()
    {
        var scenario = await RunHandleContractScenarioAsync(planned =>
        [
            .. planned,
            planned[0] with { HandleId = "confirmed-extra", ResourceId = "memory-extra" }
        ]);

        scenario.Exception.Should().NotBeNull();
        scenario.Exception!.Code.Should().Be("handle-contract");
        scenario.Revoked.Should().BeTrue();
        scenario.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task MemoryRecall_DuplicateConfirmedResourceId_ThrowsAndCompensates()
    {
        var scenario = await RunHandleContractScenarioAsync(planned =>
        [
            planned[0] with { HandleId = "confirmed-first" },
            planned[0] with { HandleId = "confirmed-duplicate" }
        ]);

        scenario.Exception.Should().NotBeNull();
        scenario.Exception!.Code.Should().Be("handle-contract");
        scenario.Revoked.Should().BeTrue();
        scenario.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task MemoryRecall_ConfirmedHandlePrincipalMismatch_ThrowsAndCompensates()
    {
        var scenario = await RunHandleContractScenarioAsync(planned =>
        [
            planned[0] with
            {
                HandleId = "confirmed-principal-mismatch",
                Principal = planned[0].Principal with { TenantId = "foreign-tenant" }
            }
        ]);

        scenario.Exception.Should().NotBeNull();
        scenario.Exception!.Code.Should().Be("handle-contract");
        scenario.Revoked.Should().BeTrue();
        scenario.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task MemoryRecall_AllReturnedItemsHaveNonEmptyCanonicalHandle()
    {
        var scenario = await RunHandleContractScenarioAsync(planned =>
        [
            planned[0] with { HandleId = "confirmed-canonical-handle" }
        ]);

        scenario.Exception.Should().BeNull();
        scenario.Outcome.Should().NotBeNull();
        scenario.Outcome!.Result.Items.Should().ContainSingle();
        scenario.Outcome.Result.Items[0].MemoryHandle.Should().Be("confirmed-canonical-handle");
        scenario.Outcome.Result.Items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.MemoryHandle));
    }

    [Fact]
    public async Task MemoryRecall_NoActiveStoredHandleIsAbsentFromOutput()
    {
        var principal = MakePrincipal();
        var descriptor = new DescriptorRef("ns", "visible", 1);
        var scope = MakeScopeWithVisibleRefs([descriptor]);
        var retriever = new Mock<IAgentMemoryRetriever>();
        retriever
            .Setup(item => item.RecallAsync(
                It.IsAny<AgentMemoryQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack
            {
                TenantId = principal.TenantId,
                Memories =
                [
                    MakeMemory("memory-stored-a", [descriptor]),
                    MakeMemory("memory-stored-b", [descriptor])
                ]
            });

        var timeProvider = TimeProvider.System;
        var handleStore = new AgentMemoryAccessHandleStore(timeProvider);
        var options = new AgentMemoryProjectionSecurityOptions();
        var coordinator = new AgentMemoryAccessArtifactCoordinator(
            handleStore,
            new AgentMemoryAccessGrantStore(timeProvider),
            new AgentMemoryAccessArtifactBatchStore(),
            new DefaultAgentMemoryArtifactLifetimePolicy(options),
            timeProvider,
            Options.Create(options));
        var core = new AgentMemoryReadCore(
            retriever.Object,
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            coordinator,
            new DefaultAgentMemoryArtifactLifetimePolicy(options),
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            timeProvider, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var outcome = await core.RecallAsync(MakeRequest(
            principal,
            MakeOrigin(),
            scope,
            new BuildAgentMemoryPackInput
            {
                MaximumCount = 5,
                CharacterBudget = 10_000
            }));

        var outputHandles = outcome.Result.Items
            .Select(item => item.MemoryHandle)
            .ToHashSet(StringComparer.Ordinal);
        outputHandles.Should().HaveCount(2);
        outcome.Receipt.HandleBatch.Should().NotBeNull();
        outcome.Receipt.HandleBatch!.Count.Should().Be(outputHandles.Count);
        foreach (var handleId in outputHandles)
        {
            var stored = await handleStore.GetAsync(handleId);
            stored.Should().NotBeNull();
            stored!.State.Should().Be(AgentMemorySecurityArtifactState.Active);
        }
    }

    [Fact]
    public async Task Recall_RetryAfterCredentialExpiry_ReturnsUsableCredentials()
    {
        var now = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now);
        var principal = MakePrincipal();
        var descriptor = new DescriptorRef("ns", "visible", 1);
        var scope = MakeScopeWithVisibleRefs([descriptor]);
        var memory = MakeMemory("memory-expiry-retry", [descriptor]);
        var retriever = new Mock<IAgentMemoryRetriever>();
        retriever
            .Setup(item => item.RecallAsync(
                It.IsAny<AgentMemoryQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack
            {
                TenantId = principal.TenantId,
                Memories = [memory]
            });
        var closureProvider = new Mock<IAgentMemoryCurrentClosureProvider>();
        closureProvider
            .Setup(item => item.GetCurrentClosureAsync(
                AgentMemoryResourceKind.Memory,
                principal.TenantId,
                memory.MemoryId,
                It.IsAny<AgentContextSourceRef?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure
            {
                TenantId = principal.TenantId,
                CurrentDescriptorRefs = [descriptor]
            });

        var handleStore = new AgentMemoryAccessHandleStore(timeProvider);
        var options = new AgentMemoryProjectionSecurityOptions();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(options);
        var coordinator = new AgentMemoryAccessArtifactCoordinator(
            handleStore,
            new AgentMemoryAccessGrantStore(timeProvider),
            new AgentMemoryAccessArtifactBatchStore(),
            lifetimePolicy,
            timeProvider,
            Options.Create(options));
        var resolver = new AgentMemoryAccessHandleResolver(
            handleStore,
            timeProvider,
            closureProvider.Object);
        var core = new AgentMemoryReadCore(
            retriever.Object,
            resolver,
            coordinator,
            lifetimePolicy,
            closureProvider.Object,
            timeProvider, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 5,
            CharacterBudget = 10_000
        };

        var first = await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        var firstHandle = first.Result.Items.Single().MemoryHandle;
        timeProvider.Advance(scope.ResourceHandleLifetime + TimeSpan.FromSeconds(1));

        var retry = await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        var retryHandle = retry.Result.Items.Single().MemoryHandle;
        var resolved = await resolver.ResolveAsync(
            retryHandle,
            AgentMemoryResourceKind.Memory,
            principal,
            scope);

        retry.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        retryHandle.Should().NotBeNullOrWhiteSpace();
        retryHandle.Should().NotBe(firstHandle);
        retry.Receipt.HandleBatch!.ReusedExisting.Should().BeFalse();
        (await handleStore.GetAsync(firstHandle)).Should().BeNull();
        resolved.Should().NotBeNull();
    }

    private static async Task<HandleContractScenario> RunHandleContractScenarioAsync(
        Func<IReadOnlyList<AgentMemoryAccessResourceHandle>, IReadOnlyList<AgentMemoryAccessResourceHandle>>
            confirmHandles)
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var memory = MakeMemory("memory-contract");
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 5,
            CharacterBudget = 10_000
        };

        var retriever = new Mock<IAgentMemoryRetriever>();
        retriever
            .Setup(item => item.RecallAsync(
                It.IsAny<AgentMemoryQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack
            {
                TenantId = principal.TenantId,
                Memories = [memory],
                WasTruncated = false
            });

        var revoked = false;
        IReadOnlyList<AgentMemoryAccessResourceHandle> confirmedHandles = [];
        var compensationToken = new AgentMemoryArtifactCompensationToken
        {
            TokenId = "handle-contract-compensation"
        };
        var coordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        coordinator
            .Setup(item => item.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(),
                It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                AgentMemoryAccessPrincipal p,
                AgentMemoryArtifactOrigin o,
                AgentMemoryAccessScope s,
                string purpose,
                int ordinal,
                IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
                IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
                CancellationToken ct) =>
            {
                confirmedHandles = confirmHandles(handles);
                return new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = new AgentMemoryAccessHandleIssueResult
                    {
                        Handles = confirmedHandles,
                        ReusedExisting = false
                    },
                    Grants = new AgentMemoryAccessGrantIssueResult
                    {
                        Grants = grants,
                        ReusedExisting = false
                    },
                    CompensationToken = compensationToken,
                    Receipt = new AgentMemoryArtifactBatchReceipt
                    {
                        HandleBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt
                        {
                            BatchHash = "handle-contract-batch",
                            Count = confirmedHandles.Count,
                            ReusedExisting = false
                        },
                        GrantBatch = null
                    }
                };
            });
        coordinator
            .Setup(item => item.RevokeCreatedAsync(
                compensationToken,
                It.IsAny<CancellationToken>()))
            .Callback(() => revoked = true)
            .Returns(ValueTask.CompletedTask);

        var core = new AgentMemoryReadCore(
            retriever.Object,
            Mock.Of<IAgentMemoryAccessHandleResolver>(),
            coordinator.Object,
            new DefaultAgentMemoryArtifactLifetimePolicy(new AgentMemoryProjectionSecurityOptions()),
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            TimeProvider.System, Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        try
        {
            var outcome = await core.RecallAsync(MakeRequest(principal, MakeOrigin(), scope, input));
            return new HandleContractScenario(outcome, null, revoked, confirmedHandles);
        }
        catch (AgentMemoryReadCoreException exception)
        {
            return new HandleContractScenario(null, exception, revoked, confirmedHandles);
        }
    }

    private sealed record HandleContractScenario(
        AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>? Outcome,
        AgentMemoryReadCoreException? Exception,
        bool Revoked,
        IReadOnlyList<AgentMemoryAccessResourceHandle> ConfirmedHandles);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
