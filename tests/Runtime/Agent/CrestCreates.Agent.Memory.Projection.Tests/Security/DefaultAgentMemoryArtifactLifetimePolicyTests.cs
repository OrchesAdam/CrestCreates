using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class DefaultAgentMemoryArtifactLifetimePolicyTests
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

    private static AgentMemoryArtifactOrigin MakeOrigin(AgentMemoryArtifactOriginKind kind)
        => new()
        {
            Kind = kind,
            BindingHash = new()
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
            VisibleDescriptorRefs = Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRef>(),
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
    public void McpInvocationHandleLifetime_CappedAt60s()
    {
        var options = new AgentMemoryProjectionSecurityOptions();
        var policy = new DefaultAgentMemoryArtifactLifetimePolicy(options);
        var scope = MakeScope();
        var origin = MakeOrigin(AgentMemoryArtifactOriginKind.McpInvocation);

        var lifetime = policy.GetHandleLifetime(MakePrincipal(), origin, scope, "recall");

        // scope.ResourceHandleLifetime is 30min, cap is 60s → 60s
        lifetime.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void McpSessionOperationHandleLifetime_CappedAtSessionCap()
    {
        var options = new AgentMemoryProjectionSecurityOptions();
        var policy = new DefaultAgentMemoryArtifactLifetimePolicy(options);
        var scope = MakeScope();
        var origin = MakeOrigin(AgentMemoryArtifactOriginKind.McpSessionOperation);

        var lifetime = policy.GetHandleLifetime(MakePrincipal(), origin, scope, "session");

        // scope.ResourceHandleLifetime is 30min, session cap is 30min → 30min
        lifetime.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void AgentToolInvocation_UsesScopeLifetime()
    {
        var options = new AgentMemoryProjectionSecurityOptions();
        var policy = new DefaultAgentMemoryArtifactLifetimePolicy(options);
        var scope = MakeScope();
        var origin = MakeOrigin(AgentMemoryArtifactOriginKind.AgentToolInvocation);

        var lifetime = policy.GetHandleLifetime(MakePrincipal(), origin, scope, "recall");

        lifetime.Should().Be(scope.ResourceHandleLifetime);
    }

    [Fact]
    public void TrustedHostOperation_UsesScopeLifetime()
    {
        var options = new AgentMemoryProjectionSecurityOptions();
        var policy = new DefaultAgentMemoryArtifactLifetimePolicy(options);
        var scope = MakeScope();
        var origin = MakeOrigin(AgentMemoryArtifactOriginKind.TrustedHostOperation);

        var lifetime = policy.GetHandleLifetime(MakePrincipal(), origin, scope, "recall");

        lifetime.Should().Be(scope.ResourceHandleLifetime);
    }

    [Fact]
    public void UnknownOriginKind_Throws()
    {
        var options = new AgentMemoryProjectionSecurityOptions();
        var policy = new DefaultAgentMemoryArtifactLifetimePolicy(options);
        var scope = MakeScope();
        var origin = MakeOrigin(AgentMemoryArtifactOriginKind.Unknown);

        var act = () => policy.GetHandleLifetime(MakePrincipal(), origin, scope, "recall");
        act.Should().Throw<InvalidOperationException>();
    }
}
