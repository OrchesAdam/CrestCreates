using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests;

public class AgentMemoryAccessScopeTests
{
    [Fact]
    public void Scope_PreservesAllBudgetFields()
    {
        var scope = new AgentMemoryAccessScope
        {
            TenantId = "t1",
            VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
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

        scope.MaxRecallCharacters.Should().Be(32_000);
        scope.MaxExpansionCharacters.Should().Be(16_000);
        scope.MaxContextRecallCharacters.Should().Be(48_000);
        scope.MaxGrantsPerOperation.Should().Be(256);
        scope.MaxResourceHandlesPerOperation.Should().Be(128);
    }

    [Fact]
    public void Scope_VisibleDescriptorRefs_IsDescriptorRefList()
    {
        var refs = new[] { new DescriptorRef("ns", "test") };
        var scope = new AgentMemoryAccessScope
        {
            TenantId = "t1",
            VisibleDescriptorRefs = refs,
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

        scope.VisibleDescriptorRefs.Should().HaveCount(1);
        scope.VisibleDescriptorRefs[0].Id.Should().Be("test");
    }
}
