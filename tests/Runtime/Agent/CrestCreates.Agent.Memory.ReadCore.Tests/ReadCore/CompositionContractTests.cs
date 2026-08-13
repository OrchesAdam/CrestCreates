using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Agent.Memory.Recall;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.ReadCore.Tests;

/// <summary>
/// Composition contract tests: real InMemory stores → real Closure Providers →
/// real ReadCore → real Coordinator → real GrantResolver → real Expander.
/// Validates the full Issue → Resolve → Expand pipeline end-to-end.
/// </summary>
public sealed class CompositionContractTests
{
    private static DescriptorRef Desc(string id, int version = 1) =>
        new() { Namespace = "test", Id = id, Version = version };

    private static CanonicalHash MakeContentHash() =>
        new()
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

    private static AgentMemoryAccessPrincipal MakePrincipal(string tenantId = "t1") =>
        new() { TenantId = tenantId, UserId = "user-1", CallerKind = AgentMemoryCallerKind.AgentTool, CallerId = "host-1", SecurityContextId = "session-1" };

    private static AgentMemoryArtifactOrigin MakeOrigin() =>
        new() { Kind = AgentMemoryArtifactOriginKind.AgentToolInvocation, BindingHash = MakeContentHash(), OperationId = "op-1" };

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
                TenantId = principal.TenantId,
                ActorId = principal.UserId ?? "user-1",
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

    private static AgentMemoryAccessScope MakeScope(DescriptorRef[] visibleRefs, bool allowUnscoped = false) =>
        new()
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

    /// <summary>
    /// Builds the full pipeline with real components. The retriever is a mock
    /// so we can control what memories are returned. The coordinator is wrapped
    /// in a capturing mock so we can extract issued grants.
    /// </summary>
    private (
        AgentMemoryReadCore core,
        AgentMemoryAccessGrantResolver grantResolver,
        DefaultAgentContextSourceExpander expander,
        InMemoryAgentConversationStore conversationStore,
        InMemoryAgentTaskHistoryStore taskStore,
        InMemoryAgentCompressedContextStore contextStore,
        InMemoryAgentMemoryStore memoryStore,
        List<AgentMemoryAccessSourceGrant> capturedGrants)
        BuildPipeline(out Mock<IAgentMemoryRetriever> retrieverMock)
    {
        var sanitizerMock = new Mock<IAgentMemoryContentSanitizer>();
        sanitizerMock
            .Setup(s => s.Sanitize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentContextSourceRef>>()))
            .Returns((string tenant, string content, IReadOnlyList<AgentContextSourceRef> refs) =>
                new SanitizedAgentContent
                {
                    SanitizedContent = content,
                    CanonicalContentHash = MakeContentHash(),
                    Rejected = false
                });
        var conversationStore = new InMemoryAgentConversationStore(sanitizerMock.Object);
        var taskStore = new InMemoryAgentTaskHistoryStore(sanitizerMock.Object);
        var contextStore = new InMemoryAgentCompressedContextStore();
        var memoryStore = new InMemoryAgentMemoryStore();

        var handleStore = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var grantStore = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchStore = new AgentMemoryAccessArtifactBatchStore();
        var securityOptions = new AgentMemoryProjectionSecurityOptions();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(securityOptions);
        var options = Microsoft.Extensions.Options.Options.Create(securityOptions);
        var realCoordinator = new AgentMemoryAccessArtifactCoordinator(
            handleStore, grantStore, batchStore, lifetimePolicy, TimeProvider.System, options);

        // Wrap coordinator to capture grants
        var capturedGrants = new List<AgentMemoryAccessSourceGrant>();
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .Callback<AgentMemoryAccessPrincipal, AgentMemoryArtifactOrigin, AgentMemoryAccessScope,
                string, int, IReadOnlyList<AgentMemoryAccessResourceHandle>, IReadOnlyList<AgentMemoryAccessSourceGrant>, CancellationToken>(
                (_, _, _, _, _, _, grants, _) => capturedGrants.AddRange(grants))
            .Returns((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string label, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> h,
                IReadOnlyList<AgentMemoryAccessSourceGrant> g, CancellationToken ct) =>
                realCoordinator.PrepareAsync(p, o, s, label, ordinal, h, g, ct));

        var closureProviders = new List<IAgentMemoryResourceClosureProvider>
        {
            new ConversationHistoryResourceClosureProvider(conversationStore),
            new TaskHistoryResourceClosureProvider(taskStore),
            new TaskEventResourceClosureProvider(taskStore),
            new ContextResourceClosureProvider(contextStore),
            new MemoryResourceClosureProvider(memoryStore),
            new CandidateResourceClosureProvider(memoryStore),
        };
        var currentClosureProvider = new CompositeCurrentClosureProvider(closureProviders);
        var handleResolver = new AgentMemoryAccessHandleResolver(handleStore, TimeProvider.System, currentClosureProvider);
        var grantResolver = new AgentMemoryAccessGrantResolver(grantStore, TimeProvider.System, currentClosureProvider);

        retrieverMock = new Mock<IAgentMemoryRetriever>();
        var core = new AgentMemoryReadCore(
            retrieverMock.Object, handleResolver, mockCoordinator.Object,
            lifetimePolicy, currentClosureProvider, TimeProvider.System,
            Mock.Of<IAgentMemoryAccountabilityProducer>(), MakeProjector());

        var expander = new DefaultAgentContextSourceExpander(
            conversationStore, taskStore, contextStore, memoryStore);

        return (core, grantResolver, expander,
            conversationStore, taskStore, contextStore, memoryStore, capturedGrants);
    }

    [Fact]
    public async Task CompressedContextBlock_IssueResolveExpand()
    {
        var (core, grantResolver, expander,
            conversationStore, taskStore, contextStore, memoryStore, capturedGrants)
            = BuildPipeline(out var retrieverMock);

        var descA = Desc("A");
        // Seed a compressed context block with a SourceRef that carries descA
        var block = new AgentCompressedContextBlock
        {
            BlockId = "block-1",
            TenantId = "t1",
            Content = "block-content",
            CanonicalContentHash = MakeContentHash(),
            SourceRefs = new[]
            {
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.ConversationTurn,
                    TenantId = "t1",
                    SourceId = "conv-1",
                    DescriptorRefs = new[] { descA }
                }
            }
        };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx-1",
            TenantId = "t1",
            Blocks = new[] { block }
        };
        await contextStore.SaveCompressedContextAsync(context);

        // Seed memory with CompressedContextBlock source ref
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "memory-content", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[]
            {
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.CompressedContextBlock,
                    TenantId = "t1",
                    SourceId = "block-1"
                }
            },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        retrieverMock.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(new[] { descA });
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Issue
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        outcome.Should().NotBeNull();
        capturedGrants.Should().ContainSingle("one grant for the CompressedContextBlock source");

        var grant = capturedGrants[0];
        grant.RequiredDescriptorRefs.Should().Contain(descA, "CompressedContextBlock grant closure must include the block's source descriptors");

        // Resolve
        var resolved = await grantResolver.ResolveAsync(grant.GrantId, principal, scope, CancellationToken.None);
        resolved.Should().NotBeNull("grant should resolve successfully");

        // Expand
        var expandRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.CompressedContextBlock,
            TenantId = "t1",
            SourceId = "block-1"
        };
        var expanded = await expander.ExpandAsync(expandRef, CancellationToken.None);
        expanded.Should().NotBeNull();
        expanded.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded, "expansion should succeed");
        expanded.SanitizedContent.Should().Be("block-content");
    }

    [Fact]
    public async Task ConversationTurnRange_IssueResolveExpand()
    {
        var (core, grantResolver, expander,
            conversationStore, taskStore, contextStore, memoryStore, capturedGrants)
            = BuildPipeline(out var retrieverMock);

        var descA = Desc("A");
        // Seed conversation
        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-1",
            TenantId = "t1",
            Turns = new[]
            {
                new AgentConversationTurn
                {
                    TurnId = "turn-0", TenantId = "t1", Role = AgentConversationRole.User,
                    Content = "hello", DescriptorRefs = new[] { descA }
                },
                new AgentConversationTurn
                {
                    TurnId = "turn-1", TenantId = "t1", Role = AgentConversationRole.Assistant,
                    Content = "world", DescriptorRefs = new[] { descA }
                }
            }
        };
        await conversationStore.SaveConversationAsync(conversation);

        // Seed memory with ConversationTurn source ref
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "memory-content", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[]
            {
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.ConversationTurn,
                    TenantId = "t1",
                    SourceId = "conv-1",
                    RangeStart = 0,
                    RangeEnd = 1
                }
            },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        retrieverMock.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(new[] { descA });
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Issue
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        outcome.Should().NotBeNull();
        capturedGrants.Should().ContainSingle();

        var grant = capturedGrants[0];
        grant.RequiredDescriptorRefs.Should().Contain(descA, "ConversationTurn grant closure must include the turn's descriptors");

        // Resolve
        var resolved = await grantResolver.ResolveAsync(grant.GrantId, principal, scope, CancellationToken.None);
        resolved.Should().NotBeNull();

        // Expand
        var expandRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1",
            SourceId = "conv-1",
            RangeStart = 0,
            RangeEnd = 1
        };
        var expanded = await expander.ExpandAsync(expandRef, CancellationToken.None);
        expanded.Should().NotBeNull();
        expanded.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
    }

    [Fact]
    public async Task InvalidRange_ReadCoreDoesNotProduceGrant()
    {
        var (core, _, _, conversationStore, _, _, _, capturedGrants)
            = BuildPipeline(out var retrieverMock);

        var descA = Desc("A");
        // Seed conversation with only 1 turn
        var conversation = new AgentConversationRecord
        {
            ConversationId = "conv-1",
            TenantId = "t1",
            Turns = new[]
            {
                new AgentConversationTurn
                {
                    TurnId = "turn-0", TenantId = "t1", Role = AgentConversationRole.User,
                    Content = "hello", DescriptorRefs = new[] { descA }
                }
            }
        };
        await conversationStore.SaveConversationAsync(conversation);

        // Memory with out-of-bounds range (only 1 turn, range [5,10] is invalid)
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "memory-content", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[]
            {
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.ConversationTurn,
                    TenantId = "t1",
                    SourceId = "conv-1",
                    RangeStart = 5,
                    RangeEnd = 10  // Invalid: conversation only has 1 turn
                }
            },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        retrieverMock.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(new[] { descA });
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Issue — invalid range should cause closure provider to return null → no grant
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        outcome.Should().NotBeNull();
        capturedGrants.Should().BeEmpty("invalid range should prevent grant issuance");
    }

    [Fact]
    public async Task TaskEventRange_IssueResolveExpand()
    {
        var (core, grantResolver, expander,
            conversationStore, taskStore, contextStore, memoryStore, capturedGrants)
            = BuildPipeline(out var retrieverMock);

        var descA = Desc("A");
        // Seed task with events that carry descriptors via SourceRefs
        var task = new AgentTaskRecord
        {
            TaskId = "task-1",
            TenantId = "t1",
            Title = "Test Task",
            Events = new[]
            {
                new AgentTaskEvent
                {
                    EventId = "evt-0", TenantId = "t1", TaskId = "task-1",
                    EventKind = "status-change", Content = "started",
                    CreatedAt = DateTimeOffset.UtcNow,
                    SourceRefs = new[]
                    {
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.ConversationTurn,
                            TenantId = "t1", SourceId = "conv-1",
                            DescriptorRefs = new[] { descA }
                        }
                    }
                },
                new AgentTaskEvent
                {
                    EventId = "evt-1", TenantId = "t1", TaskId = "task-1",
                    EventKind = "status-change", Content = "completed",
                    CreatedAt = DateTimeOffset.UtcNow,
                    SourceRefs = new[]
                    {
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.ConversationTurn,
                            TenantId = "t1", SourceId = "conv-1",
                            DescriptorRefs = new[] { descA }
                        }
                    }
                }
            }
        };
        await taskStore.SaveTaskAsync(task);

        // Seed memory with TaskEvent source ref
        var memory = new AgentMemoryItem
        {
            MemoryId = "m1", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact,
            Content = "memory-content", CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[]
            {
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.TaskEvent,
                    TenantId = "t1",
                    SourceId = "task-1",
                    RangeStart = 0,
                    RangeEnd = 1
                }
            },
            Confidence = AgentMemoryConfidence.High, Status = AgentMemoryStatus.Active
        };

        retrieverMock.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack { TenantId = "t1", Memories = [memory], WasTruncated = false });

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(new[] { descA });
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Issue
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        outcome.Should().NotBeNull();
        capturedGrants.Should().ContainSingle();

        var grant = capturedGrants[0];
        grant.RequiredDescriptorRefs.Should().Contain(descA, "TaskEvent grant closure must include the event's source descriptors");

        // Resolve
        var resolved = await grantResolver.ResolveAsync(grant.GrantId, principal, scope, CancellationToken.None);
        resolved.Should().NotBeNull();

        // Expand
        var expandRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.TaskEvent,
            TenantId = "t1",
            SourceId = "task-1",
            RangeStart = 0,
            RangeEnd = 1
        };
        var expanded = await expander.ExpandAsync(expandRef, CancellationToken.None);
        expanded.Should().NotBeNull();
        expanded.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
    }

    [Fact]
    public async Task TaskRecord_WithSourceDescriptor_RealCoordinator_PreparesAndResolves()
    {
        // TaskRecord: ScopeBinding=ResourceBound, ClosurePolicy=ExistenceOnly
        // SourceRef.DescriptorRefs=[C] but RequiredDescriptorRefs=[] (ExistenceOnly)
        // Real Coordinator must accept this — ExistenceOnly does not require
        // SourceRef.DescriptorRefs ⊆ RequiredDescriptorRefs.
        var (core, grantResolver, expander,
            conversationStore, taskStore, contextStore, memoryStore, capturedGrants)
            = BuildPipeline(out var retrieverMock);

        var descA = Desc("A");
        var descC = Desc("C");

        // Seed a task record with descriptor [C]
        var taskRecord = new AgentTaskRecord
        {
            TaskId = "task-tr-1",
            TenantId = "t1",
            Title = "Test Task",
            Events = Array.Empty<AgentTaskEvent>()
        };
        await taskStore.SaveTaskAsync(taskRecord);

        // Memory with TaskRecord source ref carrying descriptor [C]
        var memory = new AgentMemoryItem
        {
            MemoryId = "m-tr-1",
            TenantId = "t1",
            Kind = AgentMemoryKind.ProjectFact,
            Content = "task-derived fact",
            CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA }, // Memory-level descriptor
            SourceRefs = new[]
            {
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.TaskRecord,
                    TenantId = "t1",
                    SourceId = "task-tr-1",
                    DescriptorRefs = new[] { descC } // Source carries [C]
                }
            },
            Confidence = AgentMemoryConfidence.High,
            Status = AgentMemoryStatus.Active
        };

        retrieverMock
            .Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack
            {
                TenantId = "t1",
                Memories = [memory],
                WasTruncated = false
            });

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope([descA, descC]); // Both A and C visible
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Issue — real Coordinator must accept ExistenceOnly grant with empty RequiredDescriptorRefs
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        outcome.Should().NotBeNull();
        capturedGrants.Should().ContainSingle();

        var grant = capturedGrants[0];
        grant.SourceRef.SourceKind.Should().Be(AgentSourceKind.TaskRecord);
        grant.RequiredDescriptorRefs.Should().BeEmpty("TaskRecord is ExistenceOnly — RequiredDescriptorRefs must be empty");
        grant.IsUnscoped.Should().BeFalse("TaskRecord is ResourceBound — IsUnscoped must be false");

        // Resolve — real GrantResolver must succeed with ExistenceOnly
        var resolved = await grantResolver.ResolveAsync(grant.GrantId, principal, scope, CancellationToken.None);
        resolved.Should().NotBeNull("ExistenceOnly grant must resolve when resource exists");
        resolved!.IsUnscoped.Should().BeFalse();
    }

    [Fact]
    public async Task TaskRecord_WithSourceDescriptor_IssueResolveExpand_Composition()
    {
        // Full Issue → Resolve → Expand chain for TaskRecord with source descriptors.
        // Validates that ExistenceOnly grants work through the entire pipeline.
        var (core, grantResolver, expander,
            conversationStore, taskStore, contextStore, memoryStore, capturedGrants)
            = BuildPipeline(out var retrieverMock);

        var descA = Desc("A");
        var descC = Desc("C");

        // Seed a task record
        var taskRecord = new AgentTaskRecord
        {
            TaskId = "task-tr-2",
            TenantId = "t1",
            Title = "Composition Task",
            Events = Array.Empty<AgentTaskEvent>()
        };
        await taskStore.SaveTaskAsync(taskRecord);

        var memory = new AgentMemoryItem
        {
            MemoryId = "m-tr-2",
            TenantId = "t1",
            Kind = AgentMemoryKind.ProjectFact,
            Content = "composition fact",
            CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = new[] { descA },
            SourceRefs = new[]
            {
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.TaskRecord,
                    TenantId = "t1",
                    SourceId = "task-tr-2",
                    DescriptorRefs = new[] { descC }
                }
            },
            Confidence = AgentMemoryConfidence.High,
            Status = AgentMemoryStatus.Active
        };

        retrieverMock
            .Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryPack
            {
                TenantId = "t1",
                Memories = [memory],
                WasTruncated = false
            });

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope([descA, descC]);
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Issue
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));
        outcome.Should().NotBeNull();
        capturedGrants.Should().ContainSingle();

        var grant = capturedGrants[0];

        // Resolve
        var resolved = await grantResolver.ResolveAsync(grant.GrantId, principal, scope, CancellationToken.None);
        resolved.Should().NotBeNull();

        // Expand — TaskRecord is NoRange, so no range needed
        var expandRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.TaskRecord,
            TenantId = "t1",
            SourceId = "task-tr-2"
        };
        var expanded = await expander.ExpandAsync(expandRef, CancellationToken.None);
        expanded.Should().NotBeNull();
        expanded.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
    }

    private (
        AgentContextReadCore contextCore,
        AgentMemoryAccessHandleResolver handleResolver,
        AgentMemoryAccessGrantResolver grantResolver,
        DefaultAgentContextSourceExpander expander,
        InMemoryAgentCompressedContextStore contextStore,
        AgentMemoryAccessHandleStore handleStore,
        AgentMemoryAccessGrantStore grantStore,
        List<AgentMemoryAccessSourceGrant> capturedGrants)
        BuildContextPipeline()
    {
        var sanitizerMock = new Mock<IAgentMemoryContentSanitizer>();
        sanitizerMock
            .Setup(s => s.Sanitize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentContextSourceRef>>()))
            .Returns((string tenant, string content, IReadOnlyList<AgentContextSourceRef> refs) =>
                new SanitizedAgentContent
                {
                    SanitizedContent = content,
                    CanonicalContentHash = MakeContentHash(),
                    Rejected = false
                });
        var conversationStore = new InMemoryAgentConversationStore(sanitizerMock.Object);
        var taskStore = new InMemoryAgentTaskHistoryStore(sanitizerMock.Object);
        var contextStore = new InMemoryAgentCompressedContextStore();
        var memoryStore = new InMemoryAgentMemoryStore();

        var handleStore = new AgentMemoryAccessHandleStore(TimeProvider.System);
        var grantStore = new AgentMemoryAccessGrantStore(TimeProvider.System);
        var batchStore = new AgentMemoryAccessArtifactBatchStore();
        var securityOptions = new AgentMemoryProjectionSecurityOptions();
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(securityOptions);
        var options = Microsoft.Extensions.Options.Options.Create(securityOptions);
        var realCoordinator = new AgentMemoryAccessArtifactCoordinator(
            handleStore, grantStore, batchStore, lifetimePolicy, TimeProvider.System, options);

        var capturedGrants = new List<AgentMemoryAccessSourceGrant>();
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator
            .Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(), It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .Callback<AgentMemoryAccessPrincipal, AgentMemoryArtifactOrigin, AgentMemoryAccessScope,
                string, int, IReadOnlyList<AgentMemoryAccessResourceHandle>, IReadOnlyList<AgentMemoryAccessSourceGrant>, CancellationToken>(
                (_, _, _, _, _, _, grants, _) => capturedGrants.AddRange(grants))
            .Returns((AgentMemoryAccessPrincipal p, AgentMemoryArtifactOrigin o, AgentMemoryAccessScope s,
                string label, int ordinal, IReadOnlyList<AgentMemoryAccessResourceHandle> h,
                IReadOnlyList<AgentMemoryAccessSourceGrant> g, CancellationToken ct) =>
                realCoordinator.PrepareAsync(p, o, s, label, ordinal, h, g, ct));

        var closureProviders = new List<IAgentMemoryResourceClosureProvider>
        {
            new ConversationHistoryResourceClosureProvider(conversationStore),
            new TaskHistoryResourceClosureProvider(taskStore),
            new TaskEventResourceClosureProvider(taskStore),
            new ContextResourceClosureProvider(contextStore),
            new MemoryResourceClosureProvider(memoryStore),
            new CandidateResourceClosureProvider(memoryStore),
        };
        var currentClosureProvider = new CompositeCurrentClosureProvider(closureProviders);
        var handleResolver = new AgentMemoryAccessHandleResolver(handleStore, TimeProvider.System, currentClosureProvider);
        var grantResolver = new AgentMemoryAccessGrantResolver(grantStore, TimeProvider.System, currentClosureProvider);

        var contextCore = new AgentContextReadCore(
            handleResolver, contextStore, mockCoordinator.Object,
            lifetimePolicy, currentClosureProvider, TimeProvider.System);

        var expander = new DefaultAgentContextSourceExpander(
            conversationStore, taskStore, contextStore, memoryStore);

        return (contextCore, handleResolver, grantResolver, expander,
            contextStore, handleStore, grantStore, capturedGrants);
    }

    [Fact]
    public async Task CtxRecall_IssuesGrantPerExpandableSource_ThenExpandSucceeds()
    {
        var (contextCore, handleResolver, grantResolver, expander,
            contextStore, handleStore, grantStore, capturedGrants)
            = BuildContextPipeline();

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var descA = Desc("A");
        var scope = MakeScope([descA]);

        var blockSourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.CompressedContextBlock,
            TenantId = "t1", SourceId = "nested-block-1",
            DescriptorRefs = new[] { descA }
        };

        var context = new AgentCompressedContext
        {
            ContextId = "ctx-1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock
                {
                    BlockId = "nested-block-1", TenantId = "t1", Content = "nested content",
                    CanonicalContentHash = MakeContentHash(),
                    SourceRefs = new[] { blockSourceRef }
                }
            }
        };
        await contextStore.SaveCompressedContextAsync(context);

        var handleFingerprint = AgentMemoryScopeFingerprint.Compute(scope);
        var handleOrigin = MakeOrigin() with { OperationId = "op-handle-0" };
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "handle-ctx-1",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "ctx-1",
            Principal = principal,
            ScopeFingerprint = handleFingerprint,
            RequiredDescriptorRefs = new[] { descA },
            IsUnscoped = false,
            IssuingOperationId = handleOrigin.OperationId,
            IssuedAt = TimeProvider.System.GetUtcNow(),
            ExpiresAt = TimeProvider.System.GetUtcNow().AddMinutes(30)
        };
        var batchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.TrustedHostOperation,
            OriginBindingHash = MakeContentHash(),
            ArtifactPurpose = "test-handle",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeContentHash()
        };
        var issueResult = await handleStore.TryIssueBatchAsync(batchKey, new[] { handle }, 64, 128, CancellationToken.None);
        issueResult.Handles.Should().NotBeNull();
        issueResult.Handles.Should().HaveCount(1);
        issueResult.Handles[0].HandleId.Should().Be("handle-ctx-1");

        var storedHandle = await handleStore.GetAsync("handle-ctx-1", CancellationToken.None);
        storedHandle.Should().NotBeNull();
        storedHandle!.State.Should().Be(AgentMemorySecurityArtifactState.Active);

        var directResolve = await handleResolver.ResolveAsync("handle-ctx-1", AgentMemoryResourceKind.Context, principal, scope, CancellationToken.None);
        directResolve.Should().NotBeNull("because handle is valid and principal/scope match");

        var input = new RecallAgentContextInput
        {
            ContextHandle = "handle-ctx-1",
            MaximumBlockCount = 10,
            CharacterBudget = 10_000
        };

        var outcome = await contextCore.RecallContextAsync(principal, origin, scope, input);
        outcome.Should().NotBeNull();
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.Blocks.Should().HaveCount(1);
        capturedGrants.Should().ContainSingle();

        var grant = capturedGrants[0];
        var resolved = await grantResolver.ResolveAsync(grant.GrantId, principal, scope, CancellationToken.None);
        resolved.Should().NotBeNull();

        var expandRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.CompressedContextBlock,
            TenantId = "t1", SourceId = "nested-block-1"
        };
        var expanded = await expander.ExpandAsync(expandRef, CancellationToken.None);
        expanded.Should().NotBeNull();
        expanded.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
    }

    [Fact]
    public async Task CtxRecall_GrantUsedByDifferentSession_Rejects()
    {
        var (contextCore, handleResolver, grantResolver, expander,
            contextStore, handleStore, grantStore, capturedGrants)
            = BuildContextPipeline();

        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var descA = Desc("A");
        var scope = MakeScope([descA]);

        var blockSourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.CompressedContextBlock,
            TenantId = "t1", SourceId = "nested-block-1",
            DescriptorRefs = new[] { descA }
        };

        var context = new AgentCompressedContext
        {
            ContextId = "ctx-1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock
                {
                    BlockId = "nested-block-1", TenantId = "t1", Content = "nested content",
                    CanonicalContentHash = MakeContentHash(),
                    SourceRefs = new[] { blockSourceRef }
                }
            }
        };
        await contextStore.SaveCompressedContextAsync(context);

        var handleFingerprint = AgentMemoryScopeFingerprint.Compute(scope);
        var handleOrigin = MakeOrigin() with { OperationId = "op-handle-0" };
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = "handle-ctx-2",
            ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = "ctx-1",
            Principal = principal,
            ScopeFingerprint = handleFingerprint,
            RequiredDescriptorRefs = new[] { descA },
            IsUnscoped = false,
            IssuingOperationId = handleOrigin.OperationId,
            IssuedAt = TimeProvider.System.GetUtcNow(),
            ExpiresAt = TimeProvider.System.GetUtcNow().AddMinutes(30)
        };
        var batchKey2 = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = AgentMemoryArtifactOriginKind.TrustedHostOperation,
            OriginBindingHash = MakeContentHash(),
            ArtifactPurpose = "test-handle-2",
            PreparationOrdinal = 0,
            ArtifactPlanHash = MakeContentHash()
        };
        await handleStore.TryIssueBatchAsync(batchKey2, new[] { handle }, 64, 128, CancellationToken.None);

        var input = new RecallAgentContextInput
        {
            ContextHandle = "handle-ctx-2",
            MaximumBlockCount = 10,
            CharacterBudget = 10_000
        };

        var outcome = await contextCore.RecallContextAsync(principal, origin, scope, input);
        capturedGrants.Should().ContainSingle();

        var grant = capturedGrants[0];
        var foreignPrincipal = MakePrincipal() with { SecurityContextId = "different-session" };
        var resolved = await grantResolver.ResolveAsync(grant.GrantId, foreignPrincipal, scope, CancellationToken.None);
        resolved.Should().BeNull();
    }
}
