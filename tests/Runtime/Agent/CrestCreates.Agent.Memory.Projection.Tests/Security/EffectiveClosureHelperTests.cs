using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public class EffectiveClosureHelperTests
{
    private static readonly DescriptorRef DescAlpha = new("ns", "alpha", 1);
    private static readonly DescriptorRef DescBeta = new("ns", "beta", 1);
    private static readonly DescriptorRef DescGamma = new("ns", "gamma", 2);
    private static readonly DescriptorRef DescDelta = new("ns", "delta", 1);

    [Fact]
    public void ComputeEffectiveClosure_NullInputs_ReturnsEmpty()
    {
        var result = EffectiveClosureHelper.ComputeEffectiveClosure(null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeEffectiveClosure_ResourceRefsOnly_ReturnsResourceRefsSorted()
    {
        var result = EffectiveClosureHelper.ComputeEffectiveClosure(
            new[] { DescBeta, DescAlpha },
            null);

        result.Should().Equal(DescAlpha, DescBeta);
    }

    [Fact]
    public void ComputeEffectiveClosure_SourceRefsOnly_ReturnsSourceRefDescriptorsSorted()
    {
        var sourceRefs = new[]
        {
            new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src1",
                DescriptorRefs = new[] { DescGamma, DescAlpha }
            }
        };

        var result = EffectiveClosureHelper.ComputeEffectiveClosure(null, sourceRefs);

        result.Should().Equal(DescAlpha, DescGamma);
    }

    [Fact]
    public void ComputeEffectiveClosure_ResourceAndSourceRefs_MergedDistinctOrdered()
    {
        var sourceRefs = new[]
        {
            new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src1",
                DescriptorRefs = new[] { DescBeta, DescGamma }
            },
            new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.MemoryItem,
                TenantId = "t1",
                SourceId = "src2",
                DescriptorRefs = new[] { DescDelta }
            }
        };

        // Resource has DescAlpha and DescBeta (duplicate with source)
        var result = EffectiveClosureHelper.ComputeEffectiveClosure(
            new[] { DescAlpha, DescBeta },
            sourceRefs);

        // Expected: DescAlpha, DescBeta, DescGamma, DescDelta
        // (DescBeta appears in both resource and source, but should be deduplicated)
        result.Should().HaveCount(4);
        result.Should().Equal(DescAlpha, DescBeta, DescDelta, DescGamma);
    }

    [Fact]
    public void ComputeEffectiveClosure_EmptySourceRefsList_NoError()
    {
        var sourceRefs = new List<AgentContextSourceRef>
        {
            new()
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src1",
                DescriptorRefs = Array.Empty<DescriptorRef>()
            }
        };

        var result = EffectiveClosureHelper.ComputeEffectiveClosure(null, sourceRefs);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeEffectiveClosure_NullDescriptorRefsInSourceRef_NoError()
    {
        var sourceRefs = new[]
        {
            new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = "t1",
                SourceId = "src1",
                DescriptorRefs = null!
            }
        };

        var result = EffectiveClosureHelper.ComputeEffectiveClosure(new[] { DescAlpha }, sourceRefs);
        result.Should().Equal(DescAlpha);
    }

    [Fact]
    public void ComputeEffectiveClosureFromBlocks_MergesAcrossBlocks()
    {
        var blocks = new[]
        {
            new AgentCompressedContextBlock
            {
                BlockId = "b1",
                TenantId = "t1",
                Content = "content1",
                CanonicalContentHash = default!,
                SourceRefs = new[]
                {
                    new AgentContextSourceRef
                    {
                        SourceKind = AgentSourceKind.ConversationTurn,
                        TenantId = "t1",
                        SourceId = "src1",
                        DescriptorRefs = new[] { DescAlpha, DescBeta }
                    }
                }
            },
            new AgentCompressedContextBlock
            {
                BlockId = "b2",
                TenantId = "t1",
                Content = "content2",
                CanonicalContentHash = default!,
                SourceRefs = new[]
                {
                    new AgentContextSourceRef
                    {
                        SourceKind = AgentSourceKind.ConversationTurn,
                        TenantId = "t1",
                        SourceId = "src2",
                        DescriptorRefs = new[] { DescGamma }
                    }
                }
            }
        };

        var result = EffectiveClosureHelper.ComputeEffectiveClosureFromBlocks(blocks);

        result.Should().HaveCount(3);
        result.Should().Equal(DescAlpha, DescBeta, DescGamma);
    }

    [Fact]
    public void ComputeEffectiveClosureFromBlocks_EmptyBlocks_ReturnsEmpty()
    {
        var result = EffectiveClosureHelper.ComputeEffectiveClosureFromBlocks(null);
        result.Should().BeEmpty();
    }
}
