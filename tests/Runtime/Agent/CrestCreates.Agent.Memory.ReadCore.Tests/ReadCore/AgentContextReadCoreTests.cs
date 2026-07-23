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
            BindingHash = MakeHash(),
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

    private static DescriptorRef MakeDesc(string id)
        => new("ns", id);

    private static AgentContextReadCore CreateCore(
        Mock<IAgentMemoryAccessHandleResolver>? mockResolver = null,
        Mock<IAgentCompressedContextStore>? mockStore = null,
        Mock<IAgentMemoryAccessArtifactCoordinator>? mockCoordinator = null,
        Mock<IAgentMemoryArtifactLifetimePolicy>? mockLifetime = null,
        Mock<IAgentMemoryCurrentClosureProvider>? mockClosure = null)
    {
        mockResolver ??= new Mock<IAgentMemoryAccessHandleResolver>();
        mockStore ??= new Mock<IAgentCompressedContextStore>();
        mockCoordinator ??= MakeMockCoordinator();
        mockLifetime ??= MakeMockLifetimePolicy();
        mockClosure ??= new Mock<IAgentMemoryCurrentClosureProvider>();

        return new AgentContextReadCore(
            mockResolver.Object, mockStore.Object,
            mockCoordinator.Object, mockLifetime.Object, mockClosure.Object,
            TimeProvider.System);
    }

    private static Mock<IAgentMemoryAccessArtifactCoordinator> MakeMockCoordinator()
    {
        var mock = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mock.Setup(c => c.PrepareAsync(
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
                new AgentMemoryAccessPreparedArtifacts
                {
                    Handles = handles.Count > 0
                        ? new AgentMemoryAccessHandleIssueResult
                        {
                            Handles = handles.ToList(),
                            ReusedExisting = false
                        }
                        : null,
                    Grants = grants.Count > 0
                        ? new AgentMemoryAccessGrantIssueResult
                        {
                            Grants = grants.ToList(),
                            ReusedExisting = false
                        }
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
                    },
                    CompensationToken = grants.Count > 0
                        ? new AgentMemoryArtifactCompensationToken { TokenId = "token-1" }
                        : null
                });
        return mock;
    }

    private static Mock<IAgentMemoryArtifactLifetimePolicy> MakeMockLifetimePolicy()
    {
        var mock = new Mock<IAgentMemoryArtifactLifetimePolicy>();
        mock.Setup(p => p.GetGrantLifetime(It.IsAny<AgentMemoryAccessPrincipal>(),
                It.IsAny<AgentMemoryArtifactOrigin>(), It.IsAny<AgentMemoryAccessScope>(), It.IsAny<string>()))
            .Returns(TimeSpan.FromMinutes(10));
        return mock;
    }

    private static Mock<IAgentMemoryAccessHandleResolver> MakeResolveSuccessMock(
        AgentMemoryAccessPrincipal principal, string handleId = "ctx1", string resourceId = "r1")
    {
        var mock = new Mock<IAgentMemoryAccessHandleResolver>();
        mock.Setup(r => r.ResolveAsync(handleId, AgentMemoryResourceKind.Context, principal,
                It.IsAny<AgentMemoryAccessScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessResolvedResource
            {
                Handle = new AgentMemoryAccessResourceHandle
                {
                    HandleId = handleId, ResourceKind = AgentMemoryResourceKind.Context,
                    ResourceId = resourceId, Principal = principal, ScopeFingerprint = "fp",
                    IssuingOperationId = "op1", IssuedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                }
            });
        return mock;
    }

    private static Mock<IAgentCompressedContextStore> MakeStoreMock(AgentCompressedContext context)
    {
        var mock = new Mock<IAgentCompressedContextStore>();
        mock.Setup(s => s.GetCompressedContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        return mock;
    }

    [Fact]
    public async Task RecallContextAsync_ValidInput_ReturnsCompletedResult()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 1000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[] { new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "hello", CanonicalContentHash = MakeHash() } }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        outcome.Should().NotBeNull();
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.Blocks.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RecallContextAsync_ZeroOrNegativeBudget_Throws(int characterBudget)
    {
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = characterBudget };
        var core = CreateCore();

        var act = async () => await core.RecallContextAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Fact]
    public async Task RecallContextAsync_BudgetExceedsMax_Throws()
    {
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 999_999 };
        var core = CreateCore();

        var act = async () => await core.RecallContextAsync(MakePrincipal(), MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Fact]
    public async Task RecallContextAsync_HandleNotResolvable_Throws()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "nope", MaximumBlockCount = 10, CharacterBudget = 100 };

        var mockResolver = new Mock<IAgentMemoryAccessHandleResolver>();
        mockResolver.Setup(r => r.ResolveAsync("nope", AgentMemoryResourceKind.Context, principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessResolvedResource?)null);

        var core = CreateCore(mockResolver: mockResolver);

        var act = async () => await core.RecallContextAsync(principal, MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task RecallContextAsync_ContextNotFound_Throws()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 100 };

        var mockResolver = MakeResolveSuccessMock(principal);
        var mockStore = new Mock<IAgentCompressedContextStore>();
        mockStore.Setup(s => s.GetCompressedContextAsync("t1", "r1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentCompressedContext?)null);

        var core = CreateCore(mockResolver: mockResolver, mockStore: mockStore);

        var act = async () => await core.RecallContextAsync(principal, MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task RecallContextAsync_Truncation_WasTruncatedTrue()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 3 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[] { new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "long content here", CanonicalContentHash = MakeHash() } }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        outcome.Result.WasTruncated.Should().BeTrue();
        outcome.Result.Blocks[0].Content.Should().Be("lon");
    }

    [Fact]
    public async Task RecallContextAsync_TenantMismatch_Throws()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 1000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "other-tenant",
            Blocks = Array.Empty<AgentCompressedContextBlock>()
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var act = async () => await core.RecallContextAsync(principal, MakeOrigin(), scope, input);
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task RecallContextAsync_CharacterBudgetOne_NoFieldLeaksFullContent()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 1 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[] { new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "secret-content", CanonicalContentHash = MakeHash() } }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        outcome.Result.Blocks[0].Content.Should().Be("s");
        outcome.Result.Blocks[0].Content.Should().NotContain("secret-content");
    }

    [Fact]
    public async Task RecallContextAsync_TotalBlockCharacters_NeverExceedBudget()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 9 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "12345", CanonicalContentHash = MakeHash() },
                new AgentCompressedContextBlock { BlockId = "b2", TenantId = "t1", Content = "67890", CanonicalContentHash = MakeHash() }
            }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        var totalChars = outcome.Result.Blocks.Sum(b => b.Content.Length);
        totalChars.Should().BeLessThanOrEqualTo(10);
        outcome.Result.WasTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task RecallContextAsync_MaximumBlockCount_Enforced()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 1, CharacterBudget = 10_000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "block1", CanonicalContentHash = MakeHash() },
                new AgentCompressedContextBlock { BlockId = "b2", TenantId = "t1", Content = "block2", CanonicalContentHash = MakeHash() }
            }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        outcome.Result.Blocks.Should().HaveCount(1);
        outcome.Result.WasTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task RecallContextAsync_BlockRange_ReturnsOnlySelectedBlocks()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 10_000, StartBlockIndex = 1, EndBlockIndexExclusive = 2 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "block1", CanonicalContentHash = MakeHash() },
                new AgentCompressedContextBlock { BlockId = "b2", TenantId = "t1", Content = "block2", CanonicalContentHash = MakeHash() },
                new AgentCompressedContextBlock { BlockId = "b3", TenantId = "t1", Content = "block3", CanonicalContentHash = MakeHash() }
            }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        outcome.Result.Blocks.Should().HaveCount(1);
        outcome.Result.Blocks[0].Content.Should().Be("block2");
        outcome.Result.BlockCount.Should().Be(3);
    }

    [Fact]
    public async Task RecallContextAsync_WithSourceRefs_IssuesGrantsPerBlock()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var descA = MakeDesc("desc-a");
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 10_000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock
                {
                    BlockId = "b1", TenantId = "t1", Content = "block content",
                    CanonicalContentHash = MakeHash(),
                    SourceRefs = new[]
                    {
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.CompressedContextBlock,
                            TenantId = "t1", SourceId = "src1",
                            DescriptorRefs = new[] { descA }
                        }
                    }
                }
            }
        };

        var mockClosure = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosure.Setup(c => c.GetCurrentClosureAsync(
                It.IsAny<AgentMemoryResourceKind>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure
            {
                CurrentDescriptorRefs = new[] { descA },
                TenantId = "t1"
            });

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context),
            mockClosure: mockClosure);

        var outcome = await core.RecallContextAsync(principal, origin, scope, input);

        outcome.Result.Blocks[0].SourceGrants.Should().HaveCount(1);
        outcome.Result.Blocks[0].SourceGrants[0].SourceKind.Should().Be(AgentMemoryToolSourceKind.CompressedContextBlock);
    }

    [Fact]
    public async Task RecallContextAsync_UnsupportedSource_DoesNotIssueGrant()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 10_000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock
                {
                    BlockId = "b1", TenantId = "t1", Content = "block content",
                    CanonicalContentHash = MakeHash(),
                    SourceRefs = new[]
                    {
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.MetadataContextPack,
                            TenantId = "t1", SourceId = "unsupported1"
                        }
                    }
                }
            }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, origin, scope, input);

        outcome.Result.Blocks[0].SourceGrants.Should().BeEmpty();
    }

    [Fact]
    public async Task RecallContextAsync_PartialFinalBlock_WasTruncatedTrue()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 7 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock { BlockId = "b1", TenantId = "t1", Content = "12345", CanonicalContentHash = MakeHash() },
                new AgentCompressedContextBlock { BlockId = "b2", TenantId = "t1", Content = "abcdef", CanonicalContentHash = MakeHash() }
            }
        };

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context));

        var outcome = await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        outcome.Result.Blocks.Should().HaveCount(2);
        outcome.Result.Blocks[0].Content.Should().Be("12345");
        outcome.Result.Blocks[1].Content.Should().Be("ab");
        outcome.Result.WasTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task RecallContextAsync_BudgetRejectedBeforeStoreAccess()
    {
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 999_999 };

        var mockStore = new Mock<IAgentCompressedContextStore>();
        var core = CreateCore(mockStore: mockStore);

        var act = async () => await core.RecallContextAsync(MakePrincipal(), MakeOrigin(), scope, input);
        await act.Should().ThrowAsync<AgentMemoryReadCoreException>();

        mockStore.Verify(s => s.GetCompressedContextAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CtxRecall_CoordinatorFailure_NoPartialArtifactsRemain()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 10_000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock
                {
                    BlockId = "b1", TenantId = "t1", Content = "content",
                    CanonicalContentHash = MakeHash(),
                    SourceRefs = new[]
                    {
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.CompressedContextBlock,
                            TenantId = "t1", SourceId = "src1"
                        }
                    }
                }
            }
        };

        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator.Setup(c => c.PrepareAsync(
                It.IsAny<AgentMemoryAccessPrincipal>(),
                It.IsAny<AgentMemoryArtifactOrigin>(),
                It.IsAny<AgentMemoryAccessScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessResourceHandle>>(),
                It.IsAny<IReadOnlyList<AgentMemoryAccessSourceGrant>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Coordinator failure"));

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context),
            mockCoordinator: mockCoordinator);

        var act = async () => await core.RecallContextAsync(principal, MakeOrigin(), scope, input);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CtxRecall_ResultMappingFailure_RevokesCreatedGrants()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new RecallAgentContextInput { ContextHandle = "ctx1", MaximumBlockCount = 10, CharacterBudget = 10_000 };
        var context = new AgentCompressedContext
        {
            ContextId = "ctx1", TenantId = "t1",
            Blocks = new[]
            {
                new AgentCompressedContextBlock
                {
                    BlockId = "b1", TenantId = "t1", Content = "content",
                    CanonicalContentHash = MakeHash(),
                    SourceRefs = new[]
                    {
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.CompressedContextBlock,
                            TenantId = "t1", SourceId = "src1"
                        }
                    }
                }
            }
        };

        var mockClosure = new Mock<IAgentMemoryCurrentClosureProvider>();
        mockClosure.Setup(c => c.GetCurrentClosureAsync(
                It.IsAny<AgentMemoryResourceKind>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryCurrentClosure
            {
                CurrentDescriptorRefs = Array.Empty<DescriptorRef>(),
                TenantId = "t1"
            });

        var compensationToken = new AgentMemoryArtifactCompensationToken { TokenId = "revoke-token-1" };
        var mockCoordinator = new Mock<IAgentMemoryAccessArtifactCoordinator>();
        mockCoordinator.Setup(c => c.PrepareAsync(
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
                Handles = null,
                Grants = new AgentMemoryAccessGrantIssueResult
                {
                    Grants = new List<AgentMemoryAccessSourceGrant>
                    {
                        new()
                        {
                            GrantId = "g1",
                            SourceRef = new AgentContextSourceRef
                            {
                                SourceKind = AgentSourceKind.CompressedContextBlock,
                                TenantId = "t1", SourceId = "src1"
                            },
                            Principal = principal,
                            ScopeFingerprint = "fp",
                            RequiredDescriptorRefs = Array.Empty<DescriptorRef>(),
                            IsUnscoped = false,
                            IssuingOperationId = "op1",
                            IssuedAt = DateTimeOffset.UtcNow,
                            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
                        }
                    },
                    ReusedExisting = false
                },
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = new AgentMemoryArtifactBatchReceipt.BatchReceipt
                    {
                        BatchHash = "batch-g1", Count = 1, ReusedExisting = false
                    }
                },
                CompensationToken = compensationToken
            });

        mockCoordinator.Setup(c => c.RevokeCreatedAsync(
                compensationToken, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var core = CreateCore(
            mockResolver: MakeResolveSuccessMock(principal),
            mockStore: MakeStoreMock(context),
            mockCoordinator: mockCoordinator,
            mockClosure: mockClosure);

        var act = async () => await core.RecallContextAsync(principal, MakeOrigin(), scope, input);

        mockCoordinator.Verify(c => c.RevokeCreatedAsync(
            compensationToken, It.IsAny<CancellationToken>()), Times.Never);
    }
}
