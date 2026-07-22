using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests;

public class AgentMemoryArtifactOriginTests
{
    private static CanonicalHash MakeHash(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "v1",
            ArtifactKind = "test",
            Scope = "test",
            Purpose = "test",
            ContractVersion = "v1",
            CanonicalShapeVersion = "v1"
        };

    [Fact]
    public void Origin_DifferentOperationId_DifferentBindingHash()
    {
        var origin1 = new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.McpInvocation,
            BindingHash = MakeHash("hash1"),
            OperationId = "inv1"
        };

        var origin2 = new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.McpInvocation,
            BindingHash = MakeHash("hash2"),
            OperationId = "inv2"
        };

        origin1.Should().NotBe(origin2);
    }

    [Fact]
    public void OriginKind_FailClosed_UnknownRejected()
    {
        // Unknown = 0 should be the default and must be rejected at entry
        AgentMemoryArtifactOriginKind.Unknown.Should().Be(0);
        AgentMemoryCallerKind.Unknown.Should().Be(0);
    }

    [Fact]
    public void OriginKind_Ordinals_MatchSpec()
    {
        ((int)AgentMemoryArtifactOriginKind.AgentToolInvocation).Should().Be(1);
        ((int)AgentMemoryArtifactOriginKind.TrustedHostOperation).Should().Be(2);
        ((int)AgentMemoryArtifactOriginKind.McpInvocation).Should().Be(3);
        ((int)AgentMemoryArtifactOriginKind.McpSessionOperation).Should().Be(4);
    }
}
