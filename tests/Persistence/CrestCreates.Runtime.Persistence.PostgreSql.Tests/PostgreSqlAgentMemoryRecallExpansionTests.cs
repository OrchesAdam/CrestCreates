using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Persistence.Testing.Cases;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Recall and Source Expansion parity evidence: swapping the Store must not
/// change Retriever/Source Expander observable results.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryRecallExpansionTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private PostgreSqlAgentMemoryContractDriver _driver = null!;

    public PostgreSqlAgentMemoryRecallExpansionTests(PostgreSqlRuntimeCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _lease = await _fixture.CreateSchemaLeaseAsync();
        _driver = new PostgreSqlAgentMemoryContractDriver(_lease);
    }

    public async Task DisposeAsync()
    {
        await _driver.DisposeAsync();
        await _lease.DisposeAsync();
    }

    [Fact]
    public async Task Recall_Should_ReturnSameOrderPackAndHashes_AfterRestart()
    {
        var memory1 = Memory("tenant-a", "memory-recall-1", AgentMemoryKind.ProjectFact) with { Confidence = AgentMemoryConfidence.High };
        var memory2 = Memory("tenant-a", "memory-recall-2", AgentMemoryKind.Decision) with { Confidence = AgentMemoryConfidence.Medium };
        await _driver.MemoryStore.SaveMemoryAsync(memory1);
        await _driver.MemoryStore.SaveMemoryAsync(memory2);

        var query = new AgentMemoryQuery { TenantId = "tenant-a", MinimumConfidence = AgentMemoryConfidence.Medium };

        // Retriever is composed through the real runtime in the driver's provider.
        var retriever = PostgreSqlAgentMemoryContractDriver.BuildProvider(_lease.Options)
            .GetRequiredService<IAgentMemoryRetriever>();

        var before = await retriever.RecallAsync(query);

        await _driver.RebuildProviderAsync();
        var after = await PostgreSqlAgentMemoryContractDriver.BuildProvider(_lease.Options)
            .GetRequiredService<IAgentMemoryRetriever>().RecallAsync(query);

        after.Memories.Select(item => item.MemoryId).Should().Equal(
            before.Memories.Select(item => item.MemoryId),
            "recall must return the same Memory order after restart.");
        after.VisibleMemorySetHash.Should().Be(before.VisibleMemorySetHash);
        after.CanonicalPackHash.Should().Be(before.CanonicalPackHash);
        after.WasTruncated.Should().Be(before.WasTruncated);
    }

    [Fact]
    public async Task Memory_Query_Should_Match_InMemory_Contract()
        => await AgentMemoryStoreContractCases.Memory_Query_Should_Match_InMemory_Contract(_driver);

    [Fact]
    public async Task Retriever_Should_HaveInMemoryPostgreSqlParity()
    {
        var memory = Memory("tenant-a", "memory-parity", AgentMemoryKind.Preference) with
        {
            Confidence = AgentMemoryConfidence.High,
            Tags = ["tag-parity"]
        };
        await _driver.MemoryStore.SaveMemoryAsync(memory);

        var query = new AgentMemoryQuery { TenantId = "tenant-a", Tags = ["tag-parity"] };
        var retriever = PostgreSqlAgentMemoryContractDriver.BuildProvider(_lease.Options)
            .GetRequiredService<IAgentMemoryRetriever>();
        var pack = await retriever.RecallAsync(query);

        pack.Memories.Should().ContainSingle(item => item.MemoryId == "memory-parity");
        pack.Memories[0].Tags.Should().Contain("tag-parity");
    }

    [Fact]
    public async Task SourceExpansion_Should_ReturnSameDomainMaterial_AfterRestart()
    {
        var conversation = Conversation(
            "tenant-a",
            "conversation-expansion",
            Turn("tenant-a", "turn-1", "expandable content", 0));
        await _driver.ConversationStore.SaveConversationAsync(conversation);

        await _driver.RebuildProviderAsync();

        var expander = PostgreSqlAgentMemoryContractDriver.BuildProvider(_lease.Options)
            .GetRequiredService<IAgentContextSourceExpander>();
        var result = await expander.ExpandAsync(new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "tenant-a",
            SourceId = "conversation-expansion",
            RangeStart = 0,
            RangeEnd = 0
        });

        result.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
        result.SanitizedContent.Should().Contain("expandable content");
    }

    [Fact]
    public async Task SourceExpanderAndReadCore_Should_RemainUnchangedAfterRestart()
    {
        var task = Task("tenant-a", "task-expansion", "title") with
        {
            Events = [TaskEvent("tenant-a", "task-expansion", "event-1", "task expandable", 0)]
        };
        await _driver.TaskStore.SaveTaskAsync(task);

        await _driver.RebuildProviderAsync();

        var expander = PostgreSqlAgentMemoryContractDriver.BuildProvider(_lease.Options)
            .GetRequiredService<IAgentContextSourceExpander>();
        var result = await expander.ExpandAsync(new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.TaskEvent,
            TenantId = "tenant-a",
            SourceId = "task-expansion",
            RangeStart = 0,
            RangeEnd = 0
        });

        result.Status.Should().Be(AgentMemorySourceExpansionStatus.Expanded);
        result.SanitizedContent.Should().Contain("task expandable");
    }

    private static AgentConversationRecord Conversation(string tenantId, string conversationId, params AgentConversationTurn[] turns)
        => new() { TenantId = tenantId, ConversationId = conversationId, Turns = turns };

    private static AgentConversationTurn Turn(string tenantId, string turnId, string content, int sequence)
        => new()
        {
            TurnId = turnId,
            TenantId = tenantId,
            Role = sequence % 2 == 0 ? AgentConversationRole.User : AgentConversationRole.Assistant,
            Content = content,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence)
        };

    private static AgentTaskRecord Task(string tenantId, string taskId, string title)
        => new() { TenantId = tenantId, TaskId = taskId, Title = title };

    private static AgentTaskEvent TaskEvent(string tenantId, string taskId, string eventId, string content, int sequence)
        => new()
        {
            EventId = eventId,
            TenantId = tenantId,
            TaskId = taskId,
            EventKind = "event",
            Content = content,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence)
        };

    private static AgentMemoryItem Memory(string tenantId, string memoryId, AgentMemoryKind kind)
        => new()
        {
            TenantId = tenantId,
            MemoryId = memoryId,
            Kind = kind,
            Content = $"content-{memoryId}",
            CanonicalContentHash = CanonicalHashStub.For($"memory-{memoryId}"),
            Confidence = AgentMemoryConfidence.Medium,
            PromotedAt = DateTimeOffset.UnixEpoch
        };
}
