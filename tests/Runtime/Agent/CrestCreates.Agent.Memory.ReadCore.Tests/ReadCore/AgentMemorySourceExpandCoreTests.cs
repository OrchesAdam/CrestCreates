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

public class AgentMemorySourceExpandCoreTests
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

    private static AgentContextSourceRef MakeSourceRef()
        => new() { SourceKind = AgentSourceKind.ConversationTurn, TenantId = "t1", SourceId = "src1" };

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
                ActorKind = "agent",
                CorrelationId = "correlation-test",
                InvocationId = origin.OperationId,
                InvocationSource = "agent"
            },
            Scope = scope,
            Input = input
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

    /// <summary>
    /// A pass-through sanitizer that keeps the expander content untouched with no
    /// redactions, so these core tests focus on budget/grant/status behavior.
    /// </summary>
    private static Mock<IAgentMemoryContentSanitizer> MakePassThroughSanitizer()
    {
        var mock = new Mock<IAgentMemoryContentSanitizer>();
        mock.Setup(s => s.Sanitize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentContextSourceRef>>()))
            .Returns((string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs) =>
                new SanitizedAgentContent
                {
                    SanitizedContent = content,
                    CanonicalContentHash = MakeContentHash()
                });
        return mock;
    }

    private static AgentMemorySourceExpandCore MakeCore(
        IAgentMemoryAccessGrantResolver resolver,
        IAgentContextSourceExpander expander,
        IAgentMemoryAccountabilityProducer producer)
        => new(
            resolver,
            expander,
            producer,
            new AgentMemoryEffectiveResultHashProjector(new DefaultCanonicalHashComputer()),
            MakePassThroughSanitizer().Object);

    private static AgentMemoryAccessSourceGrant MakeGrant(
        AgentMemoryAccessPrincipal principal,
        AgentContextSourceRef sourceRef)
        => new()
        {
            GrantId = "g1",
            SourceRef = sourceRef,
            Principal = principal,
            ScopeFingerprint = "fp",
            IssuingOperationId = "op1",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

    [Fact]
    public async Task ExpandAsync_ValidGrant_ReturnsOutcomeWithNullCompensationToken()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 1000 };

        var mockResolver = new Mock<IAgentMemoryAccessGrantResolver>();
        mockResolver.Setup(r => r.ResolveAsync("g1", principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeGrant(principal, MakeSourceRef()));

        var mockExpander = new Mock<IAgentContextSourceExpander>();
        mockExpander.Setup(e => e.ExpandAsync(It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSourceExpansionResult
            {
                SourceRef = MakeSourceRef(),
                Status = AgentMemorySourceExpansionStatus.Expanded,
                SanitizedContent = "expanded content"
            });

        var core = MakeCore(mockResolver.Object, mockExpander.Object, Mock.Of<IAgentMemoryAccountabilityProducer>());

        var outcome = await core.ExpandAsync(MakeRequest(principal, MakeOrigin(), scope, input));

        outcome.Should().NotBeNull();
        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        outcome.CompensationToken.Should().BeNull(); // Zero artifact writes
    }

    [Fact]
    public async Task ExpandAsync_BudgetExceedsMax_Throws()
    {
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 999_999 };
        var core = MakeCore(
            Mock.Of<IAgentMemoryAccessGrantResolver>(),
            Mock.Of<IAgentContextSourceExpander>(),
            Mock.Of<IAgentMemoryAccountabilityProducer>());

        var act = async () => await core.ExpandAsync(MakeRequest(MakePrincipal(), MakeOrigin(), scope, input));
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExpandAsync_ZeroOrNegativeBudget_Throws(int maximumCharacters)
    {
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = maximumCharacters };
        var core = MakeCore(
            Mock.Of<IAgentMemoryAccessGrantResolver>(),
            Mock.Of<IAgentContextSourceExpander>(),
            Mock.Of<IAgentMemoryAccountabilityProducer>());

        var act = async () => await core.ExpandAsync(MakeRequest(MakePrincipal(), MakeOrigin(), scope, input));
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("budget-invalid");
    }

    [Fact]
    public async Task ExpandAsync_GrantNotResolvable_Throws()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "bad", MaximumCharacters = 100 };

        var mockResolver = new Mock<IAgentMemoryAccessGrantResolver>();
        mockResolver.Setup(r => r.ResolveAsync("bad", principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMemoryAccessSourceGrant?)null);

        var core = MakeCore(mockResolver.Object, Mock.Of<IAgentContextSourceExpander>(), Mock.Of<IAgentMemoryAccountabilityProducer>());

        var act = async () => await core.ExpandAsync(MakeRequest(principal, MakeOrigin(), scope, input));
        var ex = await act.Should().ThrowAsync<AgentMemoryReadCoreException>();
        ex.And.Code.Should().Be("resource-unavailable");
    }

    [Fact]
    public async Task ExpandAsync_RedactedStatus_ReturnsRedacted()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 100 };

        var mockResolver = new Mock<IAgentMemoryAccessGrantResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeGrant(principal, MakeSourceRef()));

        var mockExpander = new Mock<IAgentContextSourceExpander>();
        mockExpander.Setup(e => e.ExpandAsync(It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSourceExpansionResult
            {
                SourceRef = MakeSourceRef(),
                Status = AgentMemorySourceExpansionStatus.Redacted
            });

        var core = MakeCore(mockResolver.Object, mockExpander.Object, Mock.Of<IAgentMemoryAccountabilityProducer>());
        var outcome = await core.ExpandAsync(MakeRequest(principal, MakeOrigin(), scope, input));

        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Redacted);
    }

    [Fact]
    public async Task ExpandAsync_NotExpandableStatus_ReturnsNotExpandable()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 100 };

        var mockResolver = new Mock<IAgentMemoryAccessGrantResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeGrant(principal, MakeSourceRef()));

        var mockExpander = new Mock<IAgentContextSourceExpander>();
        mockExpander.Setup(e => e.ExpandAsync(It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSourceExpansionResult
            {
                SourceRef = MakeSourceRef(),
                Status = AgentMemorySourceExpansionStatus.NotExpandable
            });

        var core = MakeCore(mockResolver.Object, mockExpander.Object, Mock.Of<IAgentMemoryAccountabilityProducer>());
        var outcome = await core.ExpandAsync(MakeRequest(principal, MakeOrigin(), scope, input));

        outcome.Result.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.NotExpandable);
    }

    [Fact]
    public async Task ExpandAsync_Truncation_WasTruncatedTrue()
    {
        var principal = MakePrincipal();
        var scope = MakeScope();
        var input = new ExpandAgentMemorySourceInput { GrantId = "g1", MaximumCharacters = 5 };

        var mockResolver = new Mock<IAgentMemoryAccessGrantResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), principal, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeGrant(principal, MakeSourceRef()));

        var mockExpander = new Mock<IAgentContextSourceExpander>();
        mockExpander.Setup(e => e.ExpandAsync(It.IsAny<AgentContextSourceRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSourceExpansionResult
            {
                SourceRef = MakeSourceRef(),
                Status = AgentMemorySourceExpansionStatus.Expanded,
                SanitizedContent = "very long content here"
            });

        var core = MakeCore(mockResolver.Object, mockExpander.Object, Mock.Of<IAgentMemoryAccountabilityProducer>());
        var outcome = await core.ExpandAsync(MakeRequest(principal, MakeOrigin(), scope, input));

        outcome.Result.WasTruncated.Should().BeTrue();
        outcome.Result.SanitizedContent.Should().HaveLength(5);
    }
}
