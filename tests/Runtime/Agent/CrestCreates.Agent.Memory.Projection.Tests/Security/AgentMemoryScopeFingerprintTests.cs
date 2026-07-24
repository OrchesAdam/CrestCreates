using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public sealed class AgentMemoryScopeFingerprintTests
{
    [Fact]
    public void ScopeFingerprint_DelimiterPlacementCannotCollide()
    {
        var scopeA = MakeScope([new DescriptorRef("a", "b:c", 1)]);
        var scopeB = MakeScope([new DescriptorRef("a:b", "c", 1)]);

        AgentMemoryScopeFingerprint.Compute(scopeA)
            .Should().NotBe(AgentMemoryScopeFingerprint.Compute(scopeB));
    }

    [Fact]
    public void ScopeFingerprint_DescriptorOrderDoesNotMatter()
    {
        var first = new DescriptorRef("namespace:one", "id|one", 1);
        var second = new DescriptorRef("namespace|two", "id:two", 2);

        var scopeA = MakeScope([first, second]);
        var scopeB = MakeScope([second, first]);

        AgentMemoryScopeFingerprint.Compute(scopeA)
            .Should().Be(AgentMemoryScopeFingerprint.Compute(scopeB));
    }

    [Fact]
    public void ScopeFingerprint_SameLogicalScopeIsDeterministic()
    {
        var scopeA = MakeScope([new DescriptorRef("ns", "id", 7)]);
        var scopeB = MakeScope([new DescriptorRef(
            string.Concat("n", "s"),
            string.Concat("i", "d"),
            7)]);

        var first = AgentMemoryScopeFingerprint.Compute(scopeA);

        first.Should().Be(AgentMemoryScopeFingerprint.Compute(scopeA));
        first.Should().Be(AgentMemoryScopeFingerprint.Compute(scopeB));
    }

    [Fact]
    public void ScopeFingerprint_AnySingleFieldChangeChangesHash()
    {
        var baseline = MakeScope([new DescriptorRef("ns", "id", 1)]);
        var baselineFingerprint = AgentMemoryScopeFingerprint.Compute(baseline);
        var variants = new[]
        {
            baseline with { TenantId = "tenant-2" },
            baseline with { AllowUnscopedMemory = true },
            baseline with { VisibleDescriptorRefs = [new DescriptorRef("ns-2", "id", 1)] },
            baseline with { VisibleDescriptorRefs = [new DescriptorRef("ns", "id-2", 1)] },
            baseline with { VisibleDescriptorRefs = [new DescriptorRef("ns", "id", 2)] },
            baseline with { VisibleDescriptorRefs = [new DescriptorRef("ns", "id")] },
            baseline with
            {
                VisibleDescriptorRefs =
                [
                    new DescriptorRef("ns", "id", 1),
                    new DescriptorRef("ns", "id-2", 1)
                ]
            }
        };

        variants.Select(AgentMemoryScopeFingerprint.Compute)
            .Should().OnlyContain(fingerprint => fingerprint != baselineFingerprint);
    }

    private static AgentMemoryAccessScope MakeScope(IReadOnlyList<DescriptorRef> descriptorRefs)
        => new()
        {
            TenantId = "tenant-1",
            VisibleDescriptorRefs = descriptorRefs,
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
}
