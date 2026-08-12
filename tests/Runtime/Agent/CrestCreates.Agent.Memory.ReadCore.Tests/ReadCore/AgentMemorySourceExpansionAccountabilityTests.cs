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
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.ReadCore.Tests.ReadCore;

/// <summary>
/// Source Expansion Accountability integration tests — verifies that the real
/// Source Expand mainline sanitizes -> truncates -> projects the exact final
/// caller-visible content hash, and publishes terminal/failed facts inside the
/// post-result fence. Only the authorized Grant SourceRef coordinates and the
/// exact caller-visible content may enter the Accountability shape; sanitizer
/// domain hashes, expander provenance and secrets must never reach the sink.
/// </summary>
public class AgentMemorySourceExpansionAccountabilityTests
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

    private static AgentContextSourceRef MakeSourceRef(
        string sourceId = "src1",
        AgentSourceKind sourceKind = AgentSourceKind.ConversationTurn,
        int? rangeStart = null,
        int? rangeEnd = null,
        string? provenanceHash = null)
        => new()
        {
            SourceKind = sourceKind,
            TenantId = "t1",
            SourceId = sourceId,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            CanonicalContentHash = provenanceHash is null ? null : new CanonicalHash
            {
                Value = provenanceHash,
                Algorithm = "SHA-256",
                AlgorithmVersion = "v1",
                ArtifactKind = "provenance",
                Scope = "provenance",
                Purpose = "provenance",
                ContractVersion = "v1",
                CanonicalShapeVersion = "v1"
            }
        };

    private static AgentMemorySourceExpansionOperationRequest MakeRequest(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        ExpandAgentMemorySourceInput input)
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

    private static CanonicalHash MakeContentHash(string? value = "abc")
        => new()
        {
            Value = value ?? "abc",
            Algorithm = "SHA-256",
            AlgorithmVersion = "v1",
            ArtifactKind = "test",
            Scope = "test",
            Purpose = "test",
            ContractVersion = "v1",
            CanonicalShapeVersion = "v1"
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

    /// <summary>
    /// A pass-through sanitizer that keeps the expander content untouched with no
    /// redactions. RedactionKinds and Diagnostics are empty so the payload
    /// Sanitization.State is "none".
    /// </summary>
    private static Mock<IAgentMemoryContentSanitizer> MakePassThroughSanitizer()
    {
        var mock = new Mock<IAgentMemoryContentSanitizer>();
        mock.Setup(s => s.Sanitize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentContextSourceRef>>()))
            .Returns((string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs) =>
                new SanitizedAgentContent
                {
                    SanitizedContent = content,
                    CanonicalContentHash = MakeContentHash("sanitizer-pass-through")
                });
        return mock;
    }

    /// <summary>
    /// Builds the Source Expand core wired to the real projector unless a mock
    /// hash computer is supplied, with a capturing producer and a pass-through
    /// sanitizer unless one is supplied.
    /// </summary>
    private static AgentMemorySourceExpandCore MakeCore(
        IAgentMemoryAccessGrantResolver resolver,
        IAgentContextSourceExpander expander,
        Mock<IAgentMemoryAccountabilityProducer> producer,
        Mock<ICanonicalHashComputer>? computer = null,
        Mock<IAgentMemoryContentSanitizer>? sanitizer = null)
    {
        var effectiveProjector = computer is null
            ? MakeRealProjector()
            : new AgentMemoryEffectiveResultHashProjector(computer.Object);

        return new AgentMemorySourceExpandCore(
            resolver,
            expander,
            producer.Object,
            effectiveProjector,
            sanitizer?.Object ?? MakePassThroughSanitizer().Object);
    }

    private sealed class CapturedExpansion
    {
        public AgentMemorySourceExpansionAccountabilityPayload? Payload { get; set; }

        public AgentMemoryOperationIdentity? Identity { get; set; }

        public AgentMemoryInvocationContext? Context { get; set; }
    }

    /// <summary>
    /// A producer mock that captures the published identity/context/payload and
    /// returns a completed ValueTask by default (Moq default for ValueTask).
    /// </summary>
    private static (Mock<IAgentMemoryAccountabilityProducer> Mock, CapturedExpansion Captures) MakeCapturingProducer()
    {
        var captures = new CapturedExpansion();
        var mock = new Mock<IAgentMemoryAccountabilityProducer>();
        mock.Setup(p => p.PublishSourceExpansionAsync(
                It.IsAny<AgentMemoryOperationIdentity>(),
                It.IsAny<AgentMemoryInvocationContext>(),
                It.IsAny<AgentMemorySourceExpansionAccountabilityPayload>()))
            .Callback<AgentMemoryOperationIdentity, AgentMemoryInvocationContext, AgentMemorySourceExpansionAccountabilityPayload>(
                (id, ctx, pl) =>
                {
                    captures.Identity = id;
                    captures.Context = ctx;
                    captures.Payload = pl;
                });
        return (mock, captures);
    }

    private static Mock<IAgentMemoryAccessGrantResolver> MakeResolver(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        AgentContextSourceRef sourceRef,
        string grantId = "g1",
        bool resolves = true)
    {
        var mock = new Mock<IAgentMemoryAccessGrantResolver>();
        mock.Setup(r => r.ResolveAsync(grantId, principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolves
                ? new AgentMemoryAccessSourceGrant
                {
                    GrantId = grantId,
                    SourceRef = sourceRef,
                    Principal = principal,
                    ScopeFingerprint = "fp",
                    IssuingOperationId = "op1",
                    IssuedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                }
                : null);
        return mock;
    }

    private static Mock<IAgentContextSourceExpander> MakeExpander(
        AgentMemorySourceExpansionStatus status,
        string? content = null,
        AgentContextSourceRef? sourceRef = null,
        IReadOnlyList<AgentMemoryDiagnostic>? diagnostics = null)
    {
        var mock = new Mock<IAgentContextSourceExpander>();
        mock.Setup(e => e.ExpandAsync(It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSourceExpansionResult
            {
                SourceRef = sourceRef ?? MakeSourceRef(),
                Status = status,
                SanitizedContent = content,
                Diagnostics = diagnostics ?? Array.Empty<AgentMemoryDiagnostic>()
            });
        return mock;
    }

    [Fact]
    public async Task Expanded_Should_RecordExactVisibleContentHash()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };
        var sourceRef = MakeSourceRef();
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);
        var resolver = MakeResolver(principal, scope, sourceRef);
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "expanded content");
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer, computer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        var identity = captures.Identity;
        var context = captures.Context;

        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.SanitizedContent.Should().Be("expanded content");
        outcome.CompensationToken.Should().BeNull();

        payload.Should().NotBeNull();
        payload!.Status.Should().Be("expanded");
        payload.SourceKind.Should().Be("ConversationTurn");
        payload.SourceId.Should().Be("src1");
        payload.RangeStart.Should().BeNull();
        payload.RangeEnd.Should().BeNull();
        payload.MaximumCharacters.Should().Be(1000);
        payload.WasTruncated.Should().BeFalse();
        payload.Sanitization.State.Should().Be("none");
        payload.Sanitization.RedactionCodes.Should().BeEmpty();
        payload.EffectiveVisibleContentHash.Should().NotBeNull();
        payload.EffectiveVisibleContentHash!.ArtifactKind.Should().Be(AgentMemoryEffectiveResultHashProjector.ContentArtifactKind);
        payload.EffectiveVisibleContentHash.Purpose.Should().Be(AgentMemoryEffectiveResultHashProjector.ContentPurpose);
        payload.EffectiveVisibleContentHash.Scope.Should().Be(AgentMemoryEffectiveResultHashProjector.ContentScope);
        payload.EffectiveVisibleContentHash.ContractVersion.Should().Be(AgentMemoryEffectiveResultHashProjector.ContentContractVersion);
        payload.EffectiveVisibleContentHash.CanonicalShapeVersion.Should().Be(AgentMemoryEffectiveResultHashProjector.ContentCanonicalShapeVersion);
        payload.OperationId.Should().Be(identity!.OperationId);
        context.Should().NotBeNull();
        context!.TenantId.Should().Be("t1");

        // The caller-visible hash IS the effective visible hash.
        outcome.Result.CanonicalContentHash.Should().NotBeNull();
        outcome.Result.CanonicalContentHash!.Value.Should().Be(payload.EffectiveVisibleContentHash.Value);

        // The effective shape contains only TenantId + Content.
        captured.Should().HaveCount(1);
        captured[0].Should().Contain("\"TenantId\":\"t1\"");
        captured[0].Should().Contain("\"Content\":\"expanded content\"");
        captured[0].Should().NotContain("src1");
        captured[0].Should().NotContain("ConversationTurn");
        captured[0].Should().NotContain("provenance");
    }

    [Fact]
    public async Task Truncated_Should_HashTruncatedSanitizedContent()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 5 };
        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);
        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "very long content here");
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer, computer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.WasTruncated.Should().BeTrue();
        outcome.Result.SanitizedContent.Should().Be("very ");

        payload.Should().NotBeNull();
        payload!.Status.Should().Be("expanded");
        payload.WasTruncated.Should().BeTrue();
        payload.MaximumCharacters.Should().Be(5);
        payload.EffectiveVisibleContentHash.Should().NotBeNull();

        // The hash is over the exact truncated value, never the full source.
        captured.Should().HaveCount(1);
        captured[0].Should().Contain("\"Content\":\"very \"");
        captured[0].Should().NotContain("long content here");
    }

    [Fact]
    public async Task SanitizerPreTruncationHash_Should_NotBeReused()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };

        // The sanitizer returns a domain hash of its own — this must never leak
        // into the Accountability effective-visible shape or the caller result.
        var sanitizer = new Mock<IAgentMemoryContentSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentContextSourceRef>>()))
            .Returns((string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs) =>
                new SanitizedAgentContent
                {
                    SanitizedContent = content,
                    CanonicalContentHash = MakeContentHash("sanitizer-domain-hash")
                });

        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "content");
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer, sanitizer: sanitizer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.CanonicalContentHash!.Value.Should().NotBe("sanitizer-domain-hash");
        payload.Should().NotBeNull();
        payload!.EffectiveVisibleContentHash.Should().NotBeNull();
        payload.EffectiveVisibleContentHash!.Value.Should().Be(outcome.Result.CanonicalContentHash.Value);
        payload.EffectiveVisibleContentHash.Value.Should().NotBe("sanitizer-domain-hash");
    }

    [Fact]
    public async Task HiddenProvenanceChange_Should_NotChangeExpansionVisibleContentHash()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };

        // Two runs whose only difference is provenance-only data on the Grant
        // SourceRef (provenance hash and range). The effective-visible shape must
        // be identical because it contains only TenantId + exact content.
        var runs = new[]
        {
            MakeResolver(principal, scope, MakeSourceRef(provenanceHash: "prov-a", rangeStart: 0, rangeEnd: 7)),
            MakeResolver(principal, scope, MakeSourceRef(provenanceHash: "prov-b", rangeStart: 0, rangeEnd: 99))
        };

        string? firstHash = null;
        var secondCaptured = new List<string>();
        var secondComputer = MakeDeterministicComputer(secondCaptured);

        foreach (var resolver in runs)
        {
            var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "same content");
            var (producer, captures) = MakeCapturingProducer();
            var core = MakeCore(resolver.Object, expander.Object, producer, computer: secondComputer);
            var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));
            var payload = captures.Payload;
            if (firstHash is null)
            {
                firstHash = outcome.Result.CanonicalContentHash!.Value;
            }
            else
            {
                outcome.Result.CanonicalContentHash!.Value.Should().Be(firstHash);
                payload!.EffectiveVisibleContentHash!.Value.Should().Be(firstHash);
            }
        }

        // One effective projection per run; both are identical pure
        // TenantId+Content shapes — provenance never enters the shape.
        secondCaptured.Should().HaveCount(2);
        secondCaptured.Should().OnlyContain(json => !json.Contains("prov-a") && !json.Contains("prov-b") && !json.Contains("RangeStart"));
    }

    [Fact]
    public async Task NotFound_Should_RecordTerminalStatusAfterValidGrant()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };
        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.NotFound);
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.NotExpandable);
        outcome.Result.SanitizedContent.Should().BeNull();
        outcome.Result.CanonicalContentHash.Should().BeNull();

        payload.Should().NotBeNull();
        payload!.Status.Should().Be("not-found");
        payload.SourceId.Should().Be("src1");
        payload.SourceKind.Should().Be("ConversationTurn");
        payload.EffectiveVisibleContentHash.Should().BeNull();
        payload.WasTruncated.Should().BeFalse();
        payload.Sanitization.State.Should().Be("none");
    }

    [Fact]
    public async Task NotExpandable_Should_RecordTerminalStatus()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };
        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.NotExpandable);
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.NotExpandable);

        payload.Should().NotBeNull();
        payload!.Status.Should().Be("not-expandable");
        payload.SourceId.Should().Be("src1");
        payload.EffectiveVisibleContentHash.Should().BeNull();
        payload.Sanitization.State.Should().Be("none");
    }

    [Fact]
    public async Task Redacted_Should_RecordRedactionState()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };
        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Redacted);
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Redacted);
        outcome.Result.SanitizedContent.Should().BeNull();

        payload.Should().NotBeNull();
        payload!.Status.Should().Be("redacted");
        payload.SourceId.Should().Be("src1");
        payload.EffectiveVisibleContentHash.Should().BeNull();
        payload.Sanitization.State.Should().Be("redacted");
    }

    [Fact]
    public async Task SanitizerRejection_Should_ChangeCallerResultToRedacted()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };

        var sanitizer = new Mock<IAgentMemoryContentSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentContextSourceRef>>()))
            .Returns((string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs) =>
                new SanitizedAgentContent
                {
                    SanitizedContent = string.Empty,
                    CanonicalContentHash = MakeContentHash("rejected"),
                    Rejected = true,
                    RedactionKinds = new[] { "credential" }
                });

        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "sensitive content");
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer, sanitizer: sanitizer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Redacted);
        outcome.Result.SanitizedContent.Should().BeNull();
        outcome.Result.CanonicalContentHash.Should().BeNull();

        payload.Should().NotBeNull();
        payload!.Status.Should().Be("redacted");
        payload.SourceId.Should().Be("src1");
        payload.EffectiveVisibleContentHash.Should().BeNull();
        payload.WasTruncated.Should().BeFalse();
        payload.Sanitization.State.Should().Be("rejected");
        payload.Sanitization.RedactionCodes.Should().Contain("credential");
    }

    [Fact]
    public async Task SecretContent_Should_NotReachAuditSink()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };

        var sanitizer = new Mock<IAgentMemoryContentSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentContextSourceRef>>()))
            .Returns((string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs) =>
                new SanitizedAgentContent
                {
                    SanitizedContent = "[REDACTED:credential]",
                    CanonicalContentHash = MakeContentHash("redacted-hash"),
                    RedactionKinds = new[] { "credential" },
                    Diagnostics = new[]
                    {
                        new AgentMemoryDiagnostic
                        {
                            Code = new DiagnosticCode("AGENT_MEMORY_CONTENT_REDACTED"),
                            Message = "credential redacted",
                            Severity = SeverityLevel.Warning
                        }
                    }
                });

        var captured = new List<string>();
        var computer = MakeDeterministicComputer(captured);
        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "connect with password=supersecret123");
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer, computer, sanitizer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        var payload = captures.Payload;
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.SanitizedContent.Should().Be("[REDACTED:credential]");

        payload.Should().NotBeNull();
        payload!.Status.Should().Be("expanded");
        payload.Sanitization.State.Should().Be("redacted");
        payload.Sanitization.RedactionCodes.Should().Contain("credential");
        payload.Sanitization.DiagnosticCodes.Should().Contain("AGENT_MEMORY_CONTENT_REDACTED");

        // The audit sink only ever sees the redacted value — never the secret.
        captured.Should().HaveCount(1);
        captured[0].Should().NotContain("supersecret123");
        captured[0].Should().NotContain("password=");
        captured[0].Should().Contain("[REDACTED:credential]");
    }

    [Fact]
    public async Task UnresolvedGrant_Should_NotRecordSourceIdentity()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "bad", MaximumCharacters = 1000 };
        var resolver = MakeResolver(principal, scope, MakeSourceRef(), grantId: "bad", resolves: false);
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "content");
        var (producer, captures) = MakeCapturingProducer();

        var core = MakeCore(resolver.Object, expander.Object, producer);

        var act = async () => await core.ExpandAsync(MakeRequest(principal, origin, scope, input));
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");

        // No source payload without a valid Grant — no SourceId is ever recorded.
        producer.Verify(p => p.PublishSourceExpansionAsync(
            It.IsAny<AgentMemoryOperationIdentity>(),
            It.IsAny<AgentMemoryInvocationContext>(),
            It.IsAny<AgentMemorySourceExpansionAccountabilityPayload>()), Times.Never);
        captures.Payload.Should().BeNull();
    }

    [Fact]
    public async Task RecorderFailure_Should_NotChangeExpansionResult()
    {
        var principal = MakePrincipal();
        var origin = MakeOrigin();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };
        var resolver = MakeResolver(principal, scope, MakeSourceRef());
        var expander = MakeExpander(AgentMemorySourceExpansionStatus.Expanded, "content");

        var producer = new Mock<IAgentMemoryAccountabilityProducer>();
        producer.Setup(p => p.PublishSourceExpansionAsync(
                It.IsAny<AgentMemoryOperationIdentity>(),
                It.IsAny<AgentMemoryInvocationContext>(),
                It.IsAny<AgentMemorySourceExpansionAccountabilityPayload>()))
            .ThrowsAsync(new InvalidOperationException("sink down"));

        var core = MakeCore(resolver.Object, expander.Object, producer);
        var outcome = await core.ExpandAsync(MakeRequest(principal, origin, scope, input));

        // A recorder failure must never change the Expansion result.
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.Result.SanitizedContent.Should().Be("content");
        outcome.Result.CanonicalContentHash.Should().NotBeNull();
    }
}
