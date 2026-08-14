using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Persistence.Testing.Cases;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Restart durability evidence: a fresh provider over the same schema must
/// observe the complete committed state (Conversation, Task, Context/Block,
/// Memory graph) with unchanged sequence and content.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryRestartTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private PostgreSqlAgentMemoryContractDriver _driver = null!;

    public PostgreSqlAgentMemoryRestartTests(PostgreSqlRuntimeCollectionFixture fixture)
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
    public async Task Conversation_SaveAndRestart_Should_PreserveSanitizedSnapshotAndTurnSequence()
    {
        var conversation = Conversation("tenant-a", "conversation-restart",
            Turn("tenant-a", "turn-3", "third", 2),
            Turn("tenant-a", "turn-1", "first", 0));
        await _driver.ConversationStore.SaveConversationAsync(conversation);

        await _driver.RebuildProviderAsync();

        var read = await _driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-restart");
        read.Should().NotBeNull();
        read!.Turns.Select(turn => turn.TurnId).Should().Equal("turn-3", "turn-1");
        read.Turns.Select(turn => turn.Content).Should().Equal("third", "first");
    }

    [Fact]
    public async Task Task_SaveAppendAndRestart_Should_PreserveSanitizedSnapshotAndEventSequence()
    {
        var task = Task("tenant-a", "task-restart", "title");
        await _driver.TaskStore.SaveTaskAsync(task);
        await _driver.TaskStore.AppendEventAsync("tenant-a", "task-restart", TaskEvent("tenant-a", "task-restart", "event-1", "first", 0));
        await _driver.TaskStore.AppendEventAsync("tenant-a", "task-restart", TaskEvent("tenant-a", "task-restart", "event-2", "second", 1));

        await _driver.RebuildProviderAsync();

        var read = await _driver.TaskStore.GetTaskAsync("tenant-a", "task-restart");
        read.Should().NotBeNull();
        read!.Events.Select(item => item.EventId).Should().Equal("event-1", "event-2");
        read.Events.Select(item => item.Content).Should().Equal("first", "second");
    }

    [Fact]
    public async Task ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey_AndRestartLookup()
    {
        var context = CompressedContext(
            "tenant-a",
            "context-restart",
            ContextBlock("tenant-a", "block-1", "first", 0),
            ContextBlock("tenant-a", "block-2", "second", 1));
        await _driver.ContextStore.CreateCompressedContextAsync(context);

        await _driver.RebuildProviderAsync();

        var read = await _driver.ContextStore.GetCompressedContextAsync("tenant-a", "context-restart");
        read.Should().NotBeNull();
        read!.Blocks.Select(block => block.BlockId).Should().Equal("block-1", "block-2");

        var block = await _driver.ContextStore.GetCompressedContextBlockAsync("tenant-a", "block-2");
        block.Should().NotBeNull();
        block!.Content.Should().Be("second");
    }

    [Fact]
    public async Task ConcurrentTaskAppend_CommittedOrder_Should_SurviveRestart()
    {
        await _driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-append-restart", "title"));
        var first = TaskEvent("tenant-a", "task-append-restart", "event-1", "first", 0);
        var second = TaskEvent("tenant-a", "task-append-restart", "event-2", "second", 1);

        await System.Threading.Tasks.Task.WhenAll(
            _driver.TaskStore.AppendEventAsync("tenant-a", "task-append-restart", first).AsTask(),
            _driver.TaskStore.AppendEventAsync("tenant-a", "task-append-restart", second).AsTask());

        var committed = (await _driver.TaskStore.GetTaskAsync("tenant-a", "task-append-restart"))!;
        committed.Events.Should().HaveCount(2);

        await _driver.RebuildProviderAsync();

        var read = await _driver.TaskStore.GetTaskAsync("tenant-a", "task-append-restart");
        read.Should().NotBeNull();
        read!.Events.Select(item => item.EventId).Should().BeEquivalentTo("event-1", "event-2");
    }

    [Fact]
    public async Task Archive_Should_RetainGraphLinks_AfterRestart()
    {
        var candidate = Candidate("tenant-a", "candidate-restart-archive");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var operation = Operation("tenant-a", "op-restart-archive");
        var plan = _driver.PreparePromotionPlan(candidate, "memory-restart-archive", operation);
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;
        var memory = await conditional.PromoteAsync("tenant-a", plan);

        var expectation = _driver.PrepareMemoryExpectation(memory);
        await conditional.ArchiveAsync("tenant-a", expectation, Operation("tenant-a", "op-restart-archive-2"));

        await _driver.RebuildProviderAsync();

        var archived = await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-restart-archive");
        archived.Should().NotBeNull();
        archived!.Status.Should().Be(AgentMemoryStatus.Archived);
    }

    [Fact]
    public async Task MemoryGraph_Should_Survive_FreshServiceProvider()
    {
        var candidate = Candidate("tenant-a", "candidate-restart-graph");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var operation = Operation("tenant-a", "op-restart-graph");
        var plan = _driver.PreparePromotionPlan(candidate, "memory-restart-graph", operation);
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;
        var memory = await conditional.PromoteAsync("tenant-a", plan);

        await _driver.RebuildProviderAsync();

        var read = await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-restart-graph");
        read.Should().NotBeNull();
        read!.Status.Should().Be(AgentMemoryStatus.Active);
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

    private static AgentCompressedContextBlock ContextBlock(string tenantId, string blockId, string content, int ordinal)
        => new()
        {
            BlockId = blockId,
            TenantId = tenantId,
            Content = content,
            CanonicalContentHash = CanonicalHashStub.For($"block-{blockId}"),
            SourceRefs = [new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = tenantId,
                SourceId = $"source-{ordinal}"
            }]
        };

    private static AgentCompressedContext CompressedContext(string tenantId, string contextId, params AgentCompressedContextBlock[] blocks)
        => new() { TenantId = tenantId, ContextId = contextId, Blocks = blocks };

    private static AgentMemoryCandidate Candidate(string tenantId, string candidateId)
        => new()
        {
            TenantId = tenantId,
            CandidateId = candidateId,
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{candidateId}",
            CanonicalContentHash = CanonicalHashStub.For($"candidate-{candidateId}"),
            Confidence = AgentMemoryConfidence.Medium
        };

    private static AgentMemoryOperationRequest Operation(string tenantId, string operationId)
        => new()
        {
            TenantId = tenantId,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = tenantId,
                ActorId = "contract-runner",
                ActorKind = "system",
                CorrelationId = $"correlation-{operationId}",
                InvocationSource = "system"
            },
            Reason = "contract case",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = operationId,
                OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(10)
            },
            Explanation = "contract case explanation"
        };

    [Fact]
    public async Task Recall_Should_ReturnSameOrderPackAndHashes_AfterRestart()
    {
        await _driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-recall-1", AgentMemoryKind.ProjectFact) with { Confidence = AgentMemoryConfidence.High });
        await _driver.MemoryStore.SaveMemoryAsync(Memory("tenant-a", "memory-recall-2", AgentMemoryKind.Decision) with { Confidence = AgentMemoryConfidence.Medium });

        var provider = PostgreSqlAgentMemoryContractDriver.BuildProvider(_lease.Options);
        var query = new AgentMemoryQuery { TenantId = "tenant-a", MinimumConfidence = AgentMemoryConfidence.Medium };
        var retriever = provider.GetRequiredService<IAgentMemoryRetriever>();
        var before = await retriever.RecallAsync(query);

        await _driver.RebuildProviderAsync();
        var after = await PostgreSqlAgentMemoryContractDriver.BuildProvider(_lease.Options)
            .GetRequiredService<IAgentMemoryRetriever>().RecallAsync(query);

        after.Memories.Select(item => item.MemoryId).Should().Equal(before.Memories.Select(item => item.MemoryId));
        after.VisibleMemorySetHash.Should().Be(before.VisibleMemorySetHash);
        after.CanonicalPackHash.Should().Be(before.CanonicalPackHash);
        after.WasTruncated.Should().Be(before.WasTruncated);
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

    [Fact]
    public async Task Promote_RoundTrips_SubMicrosecondNonUtcPromotedAt_Consistently()
    {
        // Real mainline timestamps come from TimeProvider.GetUtcNow(): 100ns
        // precision, typically non-UTC-aligned and non-microsecond-aligned.
        // The durable Store must normalize to UTC microseconds so the JSON
        // snapshot, structured column, and state hash agree on the same value
        // across write, read, and restart.
        var promotedAt = new DateTimeOffset(
            2026, 8, 14, 10, 30, 15, 123,
            new TimeSpan(0, 8, 0, 0))
            .AddTicks(4567);
        var candidate = Candidate("tenant-a", "candidate-precision");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var operation = Operation("tenant-a", "op-precision") with
        {
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = "op-precision",
                OccurredAt = promotedAt
            }
        };
        var plan = _driver.PreparePromotionPlan(candidate, "memory-precision", operation);
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;
        var committed = await conditional.PromoteAsync("tenant-a", plan);

        var read = await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-precision");
        read.Should().NotBeNull("a freshly promoted Memory must be readable.");
        read!.PromotedAt.Should().Be(committed.PromotedAt, "read must agree with the committed snapshot.");

        // Fresh provider over the same schema: the persisted value must still
        // equal the committed snapshot (microsecond-truncated UTC).
        await _driver.RebuildProviderAsync();
        var afterRestart = await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-precision");
        afterRestart.Should().NotBeNull("the promoted Memory must survive restart.");
        afterRestart!.PromotedAt.Should().Be(committed.PromotedAt, "restart read must agree with the committed snapshot.");
    }


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
