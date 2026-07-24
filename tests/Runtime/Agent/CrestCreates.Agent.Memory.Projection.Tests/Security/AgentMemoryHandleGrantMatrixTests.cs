using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public sealed class AgentMemoryHandleGrantMatrixTests
{
    [Theory]
    [InlineData(AgentSourceKind.ConversationTurn)]
    [InlineData(AgentSourceKind.TaskRecord)]
    [InlineData(AgentSourceKind.TaskEvent)]
    public void ResourceBoundGrant_WithEmptyClosure_IsNotUnscoped(AgentSourceKind sourceKind)
    {
        AgentMemoryHandleGrantMatrix.IsUnscopedGrant(
                sourceKind,
                Array.Empty<DescriptorRef>())
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(AgentSourceKind.CompressedContextBlock)]
    [InlineData(AgentSourceKind.MemoryItem)]
    [InlineData(AgentSourceKind.MemoryCandidate)]
    public void DescriptorBoundGrant_WithEmptyClosure_IsUnscoped(AgentSourceKind sourceKind)
    {
        AgentMemoryHandleGrantMatrix.IsUnscopedGrant(
                sourceKind,
                Array.Empty<DescriptorRef>())
            .Should().BeTrue();
    }

    [Fact]
    public void ExistenceOnlyGrant_DoesNotCarryDescriptorClosure()
    {
        var descriptor = new DescriptorRef("test", "descriptor", 1);

        AgentMemoryHandleGrantMatrix.GetRequiredDescriptorRefs(
                AgentSourceKind.TaskRecord,
                [descriptor])
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(AgentSourceKind.ConversationTurn)]
    [InlineData(AgentSourceKind.TaskEvent)]
    [InlineData(AgentSourceKind.CompressedContextBlock)]
    [InlineData(AgentSourceKind.MemoryItem)]
    [InlineData(AgentSourceKind.MemoryCandidate)]
    public void ExactGrant_CarriesCurrentDescriptorClosure(AgentSourceKind sourceKind)
    {
        var descriptor = new DescriptorRef("test", "descriptor", 1);

        AgentMemoryHandleGrantMatrix.GetRequiredDescriptorRefs(sourceKind, [descriptor])
            .Should().Equal(descriptor);
    }
}
