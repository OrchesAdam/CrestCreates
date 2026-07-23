using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.Security;

public sealed class ConversationHistoryResourceClosureProviderTests
{
    private static DescriptorRef Desc(string id, int version = 1) =>
        new() { Namespace = "test", Id = id, Version = version };

    private static AgentConversationTurn MakeTurn(
        string turnId,
        DescriptorRef[]? descriptorRefs = null,
        AgentContextSourceRef[]? sourceRefs = null) =>
        new()
        {
            TurnId = turnId,
            TenantId = "t1",
            Role = AgentConversationRole.User,
            Content = $"content-{turnId}",
            DescriptorRefs = descriptorRefs ?? Array.Empty<DescriptorRef>(),
            SourceRefs = sourceRefs ?? Array.Empty<AgentContextSourceRef>()
        };

    private static AgentConversationRecord MakeConversation(
        string conversationId,
        params AgentConversationTurn[] turns) =>
        new()
        {
            ConversationId = conversationId,
            TenantId = "t1",
            Turns = turns
        };

    [Fact]
    public async Task ConversationTurn_DirectA_NestedSourceB_ClosureContainsAAndB()
    {
        // Turn has DescriptorRefs=[A] and SourceRefs with DescriptorRefs=[B]
        // Effective closure must contain both A and B
        var descA = Desc("A");
        var descB = Desc("B");
        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.CompressedContextBlock,
            TenantId = "t1",
            SourceId = "block-1",
            DescriptorRefs = new[] { descB }
        };
        var turn = MakeTurn("turn-1", descriptorRefs: [descA], sourceRefs: [sourceRef]);
        var conversation = MakeConversation("conv-1", turn);

        var mockStore = new Mock<IAgentConversationStore>();
        mockStore
            .Setup(s => s.GetConversationAsync("t1", "conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);

        var provider = new ConversationHistoryResourceClosureProvider(mockStore.Object);

        var closure = await provider.GetCurrentClosureAsync("t1", "conv-1");
        closure.Should().NotBeNull();
        closure!.CurrentDescriptorRefs.Should().Contain(descA, "direct descriptor A must be in closure");
        closure.CurrentDescriptorRefs.Should().Contain(descB, "nested source descriptor B must be in closure");
    }

    [Fact]
    public async Task ConversationTurn_ScopeOnlyA_DoesNotIssueGrant()
    {
        // Turn has DescriptorRefs=[A,B] but scope only allows [A]
        // The closure contains [A,B] which is not a subset of scope's [A]
        // This test validates the closure computation — the grant rejection is tested in ReadCore
        var descA = Desc("A");
        var descB = Desc("B");
        var turn = MakeTurn("turn-1", descriptorRefs: [descA, descB]);
        var conversation = MakeConversation("conv-1", turn);

        var mockStore = new Mock<IAgentConversationStore>();
        mockStore
            .Setup(s => s.GetConversationAsync("t1", "conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);

        var provider = new ConversationHistoryResourceClosureProvider(mockStore.Object);

        var closure = await provider.GetCurrentClosureAsync("t1", "conv-1");
        closure.Should().NotBeNull();
        // Closure contains both A and B — not a subset of scope [A]
        closure!.CurrentDescriptorRefs.Should().HaveCount(2);
        closure.CurrentDescriptorRefs.Should().Contain(descA);
        closure.CurrentDescriptorRefs.Should().Contain(descB);
    }

    [Fact]
    public async Task ConversationTurn_DescriptorOutsideSelectedRange_DoesNotInvalidateGrant()
    {
        // Conversation has 3 turns: turn-0 has [A], turn-1 has [B], turn-2 has [C]
        // Range selects only turn-1 → closure = [B]
        // Descriptor [A] outside the range must NOT appear in the closure
        var descA = Desc("A");
        var descB = Desc("B");
        var descC = Desc("C");
        var conversation = MakeConversation("conv-1",
            MakeTurn("turn-0", descriptorRefs: [descA]),
            MakeTurn("turn-1", descriptorRefs: [descB]),
            MakeTurn("turn-2", descriptorRefs: [descC]));

        var mockStore = new Mock<IAgentConversationStore>();
        mockStore
            .Setup(s => s.GetConversationAsync("t1", "conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);

        var provider = new ConversationHistoryResourceClosureProvider(mockStore.Object);

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "t1",
            SourceId = "conv-1",
            RangeStart = 1,
            RangeEnd = 1
        };

        var closure = await provider.GetCurrentClosureAsync("t1", "conv-1", sourceRef);
        closure.Should().NotBeNull();
        closure!.CurrentDescriptorRefs.Should().ContainSingle(d => d.Id == "B",
            "only turn-1's descriptor [B] should be in the range-selected closure");
        closure.CurrentDescriptorRefs.Should().NotContain(d => d.Id == "A",
            "turn-0's descriptor [A] is outside the range");
        closure.CurrentDescriptorRefs.Should().NotContain(d => d.Id == "C",
            "turn-2's descriptor [C] is outside the range");
    }
}
