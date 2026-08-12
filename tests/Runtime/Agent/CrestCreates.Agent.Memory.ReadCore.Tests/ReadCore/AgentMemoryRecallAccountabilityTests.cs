using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.ReadCore.Tests.ReadCore;

/// <summary>
/// Recall Accountability integration tests — verifies that the real Recall
/// mainline projects governed effective-visible hashes and publishes
/// completed/rejected facts inside the post-result fence, without leaking
/// MemoryIds, Handles, SourceRefs, Retriever hashes, or domain
/// CanonicalContentHash values into the Accountability shapes.
/// </summary>
public class AgentMemoryRecallAccountabilityTests
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
                ActorKind = "User"
            },
            Scope = scope,
            Input = input
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

    private static AgentMemoryItem MakeMemory(
        string id,
        string? content = null,
        string tenantId = "t1",
        IReadOnlyList<DescriptorRef>? refs = null,
        IReadOnlyList<string>? tags = null)
        => new()
        {
            MemoryId = id,
            TenantId = tenantId,
            Kind = AgentMemoryKind.ProjectFact,
            Content = content ?? "test content",
            CanonicalContentHash = MakeContentHash(),
            PromotedAt = DateTimeOffset.UtcNow,
            DescriptorRefs = refs ?? new[] { new DescriptorRef("ns", "visible1") },
            Tags = tags ?? Array.Empty<string>(),
            Confidence = AgentMemoryConfidence.High,
            Status = AgentMemoryStatus.Active
        };

    private static AgentMemoryEffectiveResultHashProjector MakeRealProjector()
        => new(new DefaultCanonicalHashComputer());

    /// <summary>
    /// A deterministic mock computer that captures the exact canonical JSON of
    /// every projection (in compute order) so tests can assert what entered the
    /// Accountability shapes, while still exercising the real canonical JSON path.
    /// </summary>
    private static Mock<ICanonicalHashComputer> MakeDeterministicComputer(List<string>? capturedProjections = null)
    {
        var mock = new Mock<ICanonicalHashComputer>();
        mock.Setup(computer => computer.ComputeFromProjection(It.IsAny<CanonicalHashProjectionResult>()))
            .Returns((CanonicalHashProjectionResult projection) =>
            {
                capturedProjections?.Add(ComputeCanonicalJson(projection));
                return new CanonicalHash
                {
                    Value = ComputeDigest(projection),
                    Algorithm = "SHA-256",
                    AlgorithmVersion = projection.Metadata.AlgorithmVersion,
                    ArtifactKind = projection.Metadata.ArtifactKind,
                    DescriptorKind = projection.Metadata.DescriptorKind,
                    Scope = projection.Metadata.Scope,
                    Purpose = projection.Metadata.Purpose,
                    ContractVersion = projection.Metadata.ContractVersion,
                    CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
                };
            });
        return mock;
    }

    private static string ComputeCanonicalJson(CanonicalHashProjectionResult projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            projection.WriteCanonicalJson(writer);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ComputeDigest(CanonicalHashProjectionResult projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            projection.WriteCanonicalJson(writer);
            writer.Flush();
        }

        var hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static Mock<IAgentMemoryAccessArtifactCoordinator> MakeCoordinator()
    {
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
        return mockCoordinator;
    }

    private static Mock<IAgentMemoryRetriever> MakeRetriever(AgentMemoryPack pack)
    {
        var mock = new Mock<IAgentMemoryRetriever>();
        mock.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pack);
        return mock;
    }

    /// <summary>
    /// Builds a ReadCore wired to the real projector unless a mock hash computer
    /// is supplied. Uses the real lifetime policy, an inert closure provider and
    /// TimeProvider.System so the Recall mainline path is exercised.
    /// </summary>
    private static AgentMemoryReadCore MakeCore(
        IAgentMemoryRetriever retriever,
        IAgentMemoryAccountabilityProducer producer,
        Mock<ICanonicalHashComputer>? computer = null,
        Mock<IAgentMemoryAccessHandleResolver>? resolver = null)
    {
        var lifetimePolicy = new DefaultAgentMemoryArtifactLifetimePolicy(new AgentMemoryProjectionSecurityOptions());
        var effectiveProjector = computer is null
            ? MakeRealProjector()
            : new AgentMemoryEffectiveResultHashProjector(computer.Object);

        return new AgentMemoryReadCore(
            retriever,
            resolver?.Object ?? Mock.Of<IAgentMemoryAccessHandleResolver>(),
            MakeCoordinator().Object,
            lifetimePolicy,
            Mock.Of<IAgentMemoryCurrentClosureProvider>(),
            TimeProvider.System,
            producer,
            effectiveProjector);
    }

    private sealed class CapturedRecall
    {
        public AgentMemoryRecallAccountabilityPayload? Payload { get; set; }

        public AgentMemoryOperationIdentity? Identity { get; set; }

        public AgentMemoryInvocationContext? Context { get; set; }
    }

    /// <summary>
    /// A producer mock that captures the published identity/context/payload and
    /// returns a completed ValueTask by default (Moq default for ValueTask).
    /// </summary>
    private static (Mock<IAgentMemoryAccountabilityProducer> Mock, CapturedRecall Captures) MakeCapturingProducer()
    {
        var captures = new CapturedRecall();
        var mock = new Mock<IAgentMemoryAccountabilityProducer>();
        mock.Setup(p => p.PublishRecallAsync(
                It.IsAny<AgentMemoryOperationIdentity>(),
                It.IsAny<AgentMemoryInvocationContext>(),
                It.IsAny<AgentMemoryRecallAccountabilityPayload>()))
            .Callback<AgentMemoryOperationIdentity, AgentMemoryInvocationContext, AgentMemoryRecallAccountabilityPayload>(
                (id, ctx, pl) =>
                {
                    captures.Identity = id;
                    captures.Context = ctx;
                    captures.Payload = pl;
                });
        return (mock, captures);
    }

    [Fact]
    public async Task Recall_Should_Record_EffectiveVisibleResult()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 5,
            CharacterBudget = 10000,
            MinimumConfidence = AgentMemoryToolConfidence.High,
            Kinds = new[] { AgentMemoryToolKind.ProjectFact }
        };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);
        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m1", "alpha memory"), MakeMemory("m2", "beta memory") },
            WasTruncated = false,
            IsAuthoritative = true
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer);
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        var identity = captures.Identity;
        var context = captures.Context;

        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.ReturnedCount.Should().Be(2);

        payload.Should().NotBeNull();
        payload!.Result.Should().Be("completed");
        payload.OperationId.Should().Be(identity!.OperationId);
        payload.ReturnedCount.Should().Be(2);
        payload.WasTruncated.Should().BeFalse();
        payload.EffectivePackHash.Should().NotBeNull();
        payload.EffectivePackHash!.ArtifactKind.Should().Be(AgentMemoryEffectiveResultHashProjector.PackArtifactKind);
        payload.EffectivePackHash.Purpose.Should().Be(AgentMemoryEffectiveResultHashProjector.PackPurpose);
        payload.EffectivePackHash.Scope.Should().Be(AgentMemoryEffectiveResultHashProjector.PackScope);
        payload.EffectivePackHash.AlgorithmVersion.Should().Be(AgentMemoryEffectiveResultHashProjector.AlgorithmVersion);
        payload.RequestedKinds.Should().Contain("ProjectFact");
        payload.MinimumConfidence.Should().Be("0.8");

        // Identity owns this Memory execution only — the logical origin is distinct.
        identity.OperationId.Should().NotBe(origin.OperationId);
        context.Should().NotBeNull();
        context!.TenantId.Should().Be("t1");

        // 2 content projections + 1 pack projection
        captured.Should().HaveCount(3);
    }

    [Fact]
    public async Task Recall_UpstreamOriginBindingMismatch_ShouldFailBeforeRetriever()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10_000 };
        var origin = MakeOrigin() with { OperationId = "origin-invocation" };
        var request = MakeRequest(principal, origin, scope, input) with
        {
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = principal.TenantId,
                ActorId = "actor-1",
                ActorKind = "agent",
                InvocationSource = "agent",
                InvocationId = "admitted-invocation"
            }
        };
        var retriever = new Mock<IAgentMemoryRetriever>(MockBehavior.Strict);
        var core = MakeCore(retriever.Object, Mock.Of<IAgentMemoryAccountabilityProducer>());

        var act = async () => await core.RecallAsync(request);

        var exception = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        exception.And.Code.Should().Be("upstream-origin-mismatch");
        retriever.Verify(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyRecall_Should_NotLeakHiddenResources()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true);
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);

        // Foreign-tenant memory is filtered out before any hash projection.
        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m-hidden", "secret", tenantId: "t2") },
            WasTruncated = false
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer);
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.Items.Should().BeEmpty();
        payload.Should().NotBeNull();
        payload!.Result.Should().Be("completed");
        payload.ReturnedCount.Should().Be(0);

        // Only the pack projection is produced — no content hash for the hidden memory.
        captured.Should().HaveCount(1);
        captured[0].Should().NotContain("m-hidden");
        captured[0].Should().NotContain("secret");
    }

    [Fact]
    public async Task TruncatedRecall_Should_Record_BudgetState()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m1") },
            WasTruncated = true
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object);
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.WasTruncated.Should().BeTrue();
        payload.Should().NotBeNull();
        payload!.Result.Should().Be("completed");
        payload.ReturnedCount.Should().Be(1);
        payload.WasTruncated.Should().BeTrue();
        payload.EffectivePackHash.Should().NotBeNull();
    }

    [Fact]
    public async Task HiddenRetrieverResult_Should_NotEnterAccountability()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(); // only [ns/visible1]
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);

        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[]
            {
                MakeMemory("m-visible", "visible content"),
                MakeMemory("m-hidden", "hidden secret", refs: new[] { new DescriptorRef("ns", "hidden1") })
            },
            WasTruncated = false
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer);
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.Items.Should().ContainSingle();

        // 1 content projection for the visible memory + 1 pack projection.
        captured.Should().HaveCount(2);
        captured[0].Should().Contain("visible content");
        captured[0].Should().NotContain("m-hidden");
        captured[0].Should().NotContain("hidden secret");
        captured[1].Should().NotContain("m-hidden");
        captured[1].Should().NotContain("hidden secret");
        payload!.ReturnedCount.Should().Be(1);
    }

    [Fact]
    public async Task HiddenQueryMemoryId_Should_NotEnterAnyAccountabilityHash()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 5,
            CharacterBudget = 10000,
            MemoryHandles = new[] { "h1" }
        };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);

        // The resolved handle exposes a hidden memory id — it must never appear
        // in any Accountability projection. The retriever returns nothing visible.
        var resolver = new Mock<IAgentMemoryAccessHandleResolver>();
        resolver.Setup(r => r.ResolveAsync("h1", AgentMemoryResourceKind.Memory, principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentMemoryAccessResolvedResource
            {
                Handle = new AgentMemoryAccessResourceHandle
                {
                    HandleId = "h1",
                    ResourceKind = AgentMemoryResourceKind.Memory,
                    ResourceId = "m-query-hidden",
                    Principal = principal,
                    ScopeFingerprint = "sf",
                    IssuingOperationId = "op1",
                    IssuedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                }
            });

        var retriever = MakeRetriever(new AgentMemoryPack { TenantId = "t1", Memories = Array.Empty<AgentMemoryItem>(), WasTruncated = false });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer, resolver);
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.Items.Should().BeEmpty();
        captured.Should().HaveCount(1); // pack projection only
        captured[0].Should().NotContain("m-query-hidden");
        payload!.ReturnedCount.Should().Be(0);
    }

    [Fact]
    public async Task RetrieverHashes_Should_NotBeReused()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);

        var retrieverHash = MakeContentHash(); // Value = "abc"
        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m1") },
            WasTruncated = false,
            VisibleMemorySetHash = retrieverHash,
            CanonicalPackHash = retrieverHash
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer);
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.Items.Should().ContainSingle();
        payload.Should().NotBeNull();
        payload!.EffectivePackHash.Should().NotBeNull();
        payload.EffectivePackHash!.Value.Should().NotBe("abc");
        payload.EffectivePackHash.ArtifactKind.Should().Be(AgentMemoryEffectiveResultHashProjector.PackArtifactKind);
    }

    [Fact]
    public async Task InternalMemoryId_Should_NotEnterEffectivePackProjection()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);

        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m-internal-1") },
            WasTruncated = false
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer);
        await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        captured.Should().HaveCount(2);
        captured[1].Should().NotContain("MemoryId");
        captured[1].Should().NotContain("m-internal-1");
        payload!.EffectivePackHash.Should().NotBeNull();
    }

    [Fact]
    public async Task DomainCanonicalContentHash_Should_NotEnterEffectivePackHash()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);

        // Domain CanonicalContentHash.Value = "abc"; raw content must not be re-hashed either.
        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m1", "raw content") },
            WasTruncated = false
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer);
        await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        captured.Should().HaveCount(2);
        captured[1].Should().NotContain("abc");
        captured[1].Should().NotContain("CanonicalContentHash");
        captured[1].Should().NotContain("raw content");
        payload!.EffectivePackHash.Should().NotBeNull();
    }

    [Fact]
    public async Task HiddenProvenanceChange_Should_NotChangeEffectiveVisibleContentHash()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Run 1: scope only exposes ns/visible1.
        var scope1 = MakeScope();
        // Run 2: scope additionally exposes ns/visible2 — the memory's own refs
        // are unchanged, so the effective-visible content hash must not change.
        var scope2 = MakeScopeWithVisibleRefs(new[]
        {
            new DescriptorRef("ns", "visible1"),
            new DescriptorRef("ns", "visible2")
        });

        async Task<(string ContentProjection, string PackHashValue, string PackProjection)> Run(AgentMemoryAccessScope scope)
        {
            var captured = new List<string>();
            var computer = MakeDeterministicComputer(captured);
            var retriever = MakeRetriever(new AgentMemoryPack
            {
                TenantId = "t1",
                Memories = new[] { MakeMemory("m1", "stable content") },
                WasTruncated = false
            });
            var (producer, captures) = MakeCapturingProducer();
            var core = MakeCore(retriever.Object, producer.Object, computer);
            await core.RecallAsync(MakeRequest(principal, origin, scope, input));
            var payload = captures.Payload;
            return (captured[0], payload!.EffectivePackHash!.Value, captured[1]);
        }

        var run1 = await Run(scope1);
        var run2 = await Run(scope2);

        run1.ContentProjection.Should().Be(run2.ContentProjection);
        run1.PackHashValue.Should().Be(run2.PackHashValue);
        run1.PackProjection.Should().NotContain("visible2");
    }

    [Fact]
    public async Task VisibleContentChange_Should_ChangeEffectiveVisibleContentHash()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        async Task<(string ContentProjection, string PackHashValue)> Run(string content)
        {
            var captured = new List<string>();
            var computer = MakeDeterministicComputer(captured);
            var retriever = MakeRetriever(new AgentMemoryPack
            {
                TenantId = "t1",
                Memories = new[] { MakeMemory("m1", content) },
                WasTruncated = false
            });
            var (producer, captures) = MakeCapturingProducer();
            var core = MakeCore(retriever.Object, producer.Object, computer);
            await core.RecallAsync(MakeRequest(principal, origin, scope, input));
            var payload = captures.Payload;
            return (captured[0], payload!.EffectivePackHash!.Value);
        }

        var run1 = await Run("original content");
        var run2 = await Run("changed content");

        run1.ContentProjection.Should().NotBe(run2.ContentProjection);
        run1.PackHashValue.Should().NotBe(run2.PackHashValue);
    }

    [Fact]
    public async Task DefenseFiltering_Should_ReprojectHashesWithCanonicalRuntime()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope(allowUnscoped: true);
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        // Real projector + real DefaultCanonicalHashComputer (computer == null).
        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[]
            {
                MakeMemory("m-visible", "visible content"),
                MakeMemory("m-foreign", "foreign secret", tenantId: "t2")
            },
            WasTruncated = false
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object);
        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.Items.Should().ContainSingle();
        payload.Should().NotBeNull();
        payload!.EffectivePackHash.Should().NotBeNull();
        payload.EffectivePackHash!.ArtifactKind.Should().Be(AgentMemoryEffectiveResultHashProjector.PackArtifactKind);
        payload.EffectivePackHash.Purpose.Should().Be(AgentMemoryEffectiveResultHashProjector.PackPurpose);
        payload.EffectivePackHash.Scope.Should().Be(AgentMemoryEffectiveResultHashProjector.PackScope);
        payload.EffectivePackHash.AlgorithmVersion.Should().Be(AgentMemoryEffectiveResultHashProjector.AlgorithmVersion);
        payload.EffectivePackHash.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Recall_Should_NotRecordRawContentIdsTagsOrIntent()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 5,
            CharacterBudget = 10000,
            Kinds = new[] { AgentMemoryToolKind.ProjectFact }
        };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);

        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[]
            {
                MakeMemory("m-raw-1", "sensitive content", tags: new[] { "secret-tag" })
            },
            WasTruncated = false
        });
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(retriever.Object, producer.Object, computer);
        await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        captured.Should().HaveCount(2);

        // Content projection carries only TenantId + exact returned Content.
        captured[0].Should().Contain("sensitive content");
        captured[0].Should().NotContain("m-raw-1");
        captured[0].Should().NotContain("secret-tag");
        captured[0].Should().NotContain("MemoryId");
        captured[0].Should().NotContain("Reason");

        // Pack projection carries no raw id/tag either.
        captured[1].Should().NotContain("m-raw-1");
        captured[1].Should().NotContain("secret-tag");

        payload.Should().NotBeNull();
        payload!.DiagnosticCodes.Should().BeEmpty();
        payload.RequestedKinds.Should().Contain("ProjectFact");
    }

    [Fact]
    public async Task StableRejectedRecall_Should_RecordSafeCode()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();

        async Task<(string? Code, AgentMemoryRecallAccountabilityPayload? Payload)> RunRejected(
            BuildAgentMemoryPackInput input,
            Mock<IAgentMemoryAccessHandleResolver>? resolver = null)
        {
            var (producer, captures) = MakeCapturingProducer();
            var retriever = MakeRetriever(new AgentMemoryPack { TenantId = "t1", Memories = Array.Empty<AgentMemoryItem>(), WasTruncated = false });
            var core = MakeCore(retriever.Object, producer.Object, resolver: resolver);

            string? code = null;
            try
            {
                await core.RecallAsync(MakeRequest(principal, origin, scope, input));
            }
            catch (AgentMemoryReadCoreException ex)
            {
                code = ex.Code;
            }

            var payload = captures.Payload;
            var identity = captures.Identity;
            if (payload is not null)
            {
                payload.OperationId.Should().Be(identity!.OperationId);
            }

            return (code, payload);
        }

        // Budget invalid — MaximumCount <= 0
        var budgetResult = await RunRejected(new BuildAgentMemoryPackInput { MaximumCount = 0, CharacterBudget = 10000 });
        budgetResult.Code.Should().Be("budget-invalid");
        budgetResult.Payload.Should().NotBeNull();
        budgetResult.Payload!.Result.Should().Be("rejected");
        budgetResult.Payload.StableFailureCode.Should().Be("budget-invalid");
        budgetResult.Payload.EffectivePackHash.Should().BeNull();
        budgetResult.Payload.ReturnedCount.Should().Be(0);
        budgetResult.Payload.WasTruncated.Should().BeFalse();

        // Resource unavailable — handle resolution fails
        var resolver = new Mock<IAgentMemoryAccessHandleResolver>();
        resolver.Setup(r => r.ResolveAsync("missing", AgentMemoryResourceKind.Memory, principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessResolvedResource?)null);
        var resourceResult = await RunRejected(
            new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000, MemoryHandles = new[] { "missing" } },
            resolver);
        resourceResult.Code.Should().Be("resource-unavailable");
        resourceResult.Payload.Should().NotBeNull();
        resourceResult.Payload!.Result.Should().Be("rejected");
        resourceResult.Payload.StableFailureCode.Should().Be("resource-unavailable");
        resourceResult.Payload.EffectivePackHash.Should().BeNull();
        resourceResult.Payload.ReturnedCount.Should().Be(0);
        resourceResult.Payload.WasTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task ProviderFailure_Should_NotClaimDeterministicFailure()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var mockRetriever = new Mock<IAgentMemoryRetriever>();
        mockRetriever.Setup(r => r.RecallAsync(It.IsAny<AgentMemoryQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("retriever exploded"));

        var (producer, captures) = MakeCapturingProducer();
        var payload = captures.Payload;
        var core = MakeCore(mockRetriever.Object, producer.Object);

        var act = async () => await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("retriever exploded");

        // Unknown provider failures produce no fabricated Recall fact.
        payload.Should().BeNull();
        producer.Verify(p => p.PublishRecallAsync(
            It.IsAny<AgentMemoryOperationIdentity>(),
            It.IsAny<AgentMemoryInvocationContext>(),
            It.IsAny<AgentMemoryRecallAccountabilityPayload>()), Times.Never);
    }

    [Fact]
    public async Task RecorderFailure_Should_NotChangeRecallResult()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m1", "content") },
            WasTruncated = false
        });

        // Producer throws inside the post-result fence — the established Recall
        // result must survive unchanged.
        var producer = new Mock<IAgentMemoryAccountabilityProducer>();
        producer.Setup(p => p.PublishRecallAsync(
                It.IsAny<AgentMemoryOperationIdentity>(),
                It.IsAny<AgentMemoryInvocationContext>(),
                It.IsAny<AgentMemoryRecallAccountabilityPayload>()))
            .Throws(new InvalidOperationException("sink down"));

        var core = MakeCore(retriever.Object, producer.Object);

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.Items.Should().ContainSingle();
        outcome.CompensationToken.Should().NotBeNull();
    }

    [Fact]
    public async Task RecallProjectionFailure_Should_NotChangeEstablishedResult()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new BuildAgentMemoryPackInput { MaximumCount = 5, CharacterBudget = 10000 };

        var retriever = MakeRetriever(new AgentMemoryPack
        {
            TenantId = "t1",
            Memories = new[] { MakeMemory("m1", "content") },
            WasTruncated = false
        });

        // Hash projection fails inside the fence — the exact Recall result
        // established before the fence must be returned unchanged.
        var computer = new Mock<ICanonicalHashComputer>();
        computer.Setup(c => c.ComputeFromProjection(It.IsAny<CanonicalHashProjectionResult>()))
            .Throws(new InvalidOperationException("projector failed"));

        var (producer, captures) = MakeCapturingProducer();
        var payload = captures.Payload;
        var core = MakeCore(retriever.Object, producer.Object, computer);

        var outcome = await core.RecallAsync(MakeRequest(principal, origin, scope, input));

        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.Items.Should().ContainSingle();
        outcome.CompensationToken.Should().NotBeNull();

        // The fence swallowed the failure before the producer call.
        payload.Should().BeNull();
        producer.Verify(p => p.PublishRecallAsync(
            It.IsAny<AgentMemoryOperationIdentity>(),
            It.IsAny<AgentMemoryInvocationContext>(),
            It.IsAny<AgentMemoryRecallAccountabilityPayload>()), Times.Never);
    }
}
