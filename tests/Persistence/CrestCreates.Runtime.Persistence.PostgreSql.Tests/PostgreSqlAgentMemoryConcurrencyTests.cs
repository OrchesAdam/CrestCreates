using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Real concurrency evidence: committed concurrent mutations must serialize
/// with exactly one valid winner and no lost state.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private PostgreSqlAgentMemoryContractDriver _driver = null!;

    public PostgreSqlAgentMemoryConcurrencyTests(PostgreSqlRuntimeCollectionFixture fixture)
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
    public async Task Concurrent_TaskAppend_Should_Not_Lose_Event()
    {
        await _driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-concurrent", "title"));
        var first = TaskEvent("tenant-a", "task-concurrent", "event-1", "first", 0);
        var second = TaskEvent("tenant-a", "task-concurrent", "event-2", "second", 1);

        await System.Threading.Tasks.Task.WhenAll(
            _driver.TaskStore.AppendEventAsync("tenant-a", "task-concurrent", first).AsTask(),
            _driver.TaskStore.AppendEventAsync("tenant-a", "task-concurrent", second).AsTask());

        var read = await _driver.TaskStore.GetTaskAsync("tenant-a", "task-concurrent");
        read.Should().NotBeNull();
        read!.Events.Should().HaveCount(2, "two committed concurrent appends must both be visible exactly once.");
        read.Events.Select(item => item.EventId).Should().BeEquivalentTo("event-1", "event-2");
    }

    [Fact]
    public async Task ConcurrentTaskAppend_CommittedOrder_Should_SurviveRestart()
    {
        await _driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-concurrent-restart", "title"));
        var first = TaskEvent("tenant-a", "task-concurrent-restart", "event-1", "first", 0);
        var second = TaskEvent("tenant-a", "task-concurrent-restart", "event-2", "second", 1);

        await System.Threading.Tasks.Task.WhenAll(
            _driver.TaskStore.AppendEventAsync("tenant-a", "task-concurrent-restart", first).AsTask(),
            _driver.TaskStore.AppendEventAsync("tenant-a", "task-concurrent-restart", second).AsTask());

        await _driver.RebuildProviderAsync();

        var read = await _driver.TaskStore.GetTaskAsync("tenant-a", "task-concurrent-restart");
        read.Should().NotBeNull();
        read!.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentPromote_Should_HaveExactlyOneWinner()
    {
        var candidate = Candidate("tenant-a", "candidate-concurrent");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var plan = _driver.PreparePromotionPlan(candidate, "memory-concurrent", Operation("tenant-a", "op-concurrent"));
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;

        var results = await System.Threading.Tasks.Task.WhenAll(
            RunPromote(conditional, plan),
            RunPromote(conditional, plan));

        results.Count(result => result).Should().Be(1, "exactly one concurrent Promote must win.");

        var memory = await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-concurrent");
        memory.Should().NotBeNull();
        var storedCandidate = await _driver.MemoryStore.GetCandidateAsync("tenant-a", "candidate-concurrent");
        storedCandidate!.Status.Should().Be(AgentMemoryStatus.Active);
    }

    [Fact]
    public async Task ConcurrentSupersedeOrArchive_Should_HaveOneValidWinner()
    {
        var candidate = Candidate("tenant-a", "candidate-supersede-concurrent");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var promotePlan = _driver.PreparePromotionPlan(candidate, "memory-supersede-target", Operation("tenant-a", "op-s-1"));
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;
        var target = await conditional.PromoteAsync("tenant-a", promotePlan);

        var replacement = Candidate("tenant-a", "candidate-supersede-repl");
        await _driver.MemoryStore.CreateCandidateAsync(replacement);
        var supersession = _driver.PrepareSupersessionPlan(
            target, replacement, "memory-supersede-new", Operation("tenant-a", "op-s-2"));
        var archiveExpectation = _driver.PrepareMemoryExpectation(target);

        var results = await System.Threading.Tasks.Task.WhenAll(
            RunSupersede(conditional, supersession),
            RunArchive(conditional, archiveExpectation, Operation("tenant-a", "op-s-3")));

        results.Count(result => result).Should().Be(1, "Supersede vs Archive on one target must have exactly one valid winner.");

        var finalTarget = await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-supersede-target");
        finalTarget!.Status.Should().BeOneOf(AgentMemoryStatus.Superseded, AgentMemoryStatus.Archived);
    }

    [Fact]
    public async Task CandidateBatch_WithOneConflict_Should_WriteNone()
    {
        await _driver.MemoryStore.CreateCandidateAsync(Candidate("tenant-a", "batch-existing"));

        var batch = new[]
        {
            Candidate("tenant-a", "batch-1"),
            Candidate("tenant-a", "batch-existing"),
            Candidate("tenant-a", "batch-3")
        };

        var act = async () => await _driver.MemoryStore.CreateCandidatesAsync(batch);
        await act.Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);

        (await _driver.MemoryStore.GetCandidateAsync("tenant-a", "batch-1")).Should().BeNull();
        (await _driver.MemoryStore.GetCandidateAsync("tenant-a", "batch-3")).Should().BeNull();
    }

    private static async Task<bool> RunPromote(IAgentMemoryConditionalCurationStore store, AgentMemoryPromotionPlan plan)
    {
        try
        {
            await store.PromoteAsync(plan.Operation.TenantId, plan);
            return true;
        }
        catch (AgentMemoryOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> RunSupersede(IAgentMemoryConditionalCurationStore store, AgentMemorySupersessionPlan plan)
    {
        try
        {
            await store.SupersedeAsync(plan.Operation.TenantId, plan);
            return true;
        }
        catch (AgentMemoryOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> RunArchive(IAgentMemoryConditionalCurationStore store, AgentMemoryItemExpectation expectation, AgentMemoryOperationRequest operation)
    {
        try
        {
            await store.ArchiveAsync(operation.TenantId, expectation, operation);
            return true;
        }
        catch (AgentMemoryOperationException)
        {
            return false;
        }
    }

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
    public async Task ConcurrentContextReplacement_And_Read_Should_Not_Report_False_Corruption()
    {
        // The replacement commits between the reader's parent query and its
        // Block projection query. Without a shared read lock, the reader sees
        // an old parent with new Blocks and reports a false persisted
        // corruption. With the Context advisory lock, the read is serialized
        // after the replacement and returns a consistent snapshot.
        var contextId = "context-concurrent-read";
        var original = Context("tenant-a", contextId, "block-v1");
        await _driver.ContextStore.CreateCompressedContextAsync(original);

        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pause the reader after its parent query completes (first command
        // done, Block projection query not yet issued). A replacement commits
        // in this window: without a shared read lock the reader would then see
        // an old parent with new Blocks and report a false corruption.
        using var hook = PostgreSqlRuntimeTestHooks.BlockAfterFirstCommand(() =>
        {
            readStarted.SetResult();
            releaseRead.Task.GetAwaiter().GetResult();
        });

        var readerTask = System.Threading.Tasks.Task.Run(async () =>
        {
            var read = await _driver.ContextStore.GetCompressedContextAsync("tenant-a", contextId);
            read.Should().NotBeNull();
            return read!;
        });

        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // The reader is paused after its parent query, holding the Context
        // lock. Start a replacement in the same window: it must block on the
        // lock until the reader finishes.
        var replacement = Context("tenant-a", contextId, "block-v2");
        var replacementTask = System.Threading.Tasks.Task.Run(
            () => _driver.ContextStore.SaveCompressedContextAsync(replacement).AsTask());
        await System.Threading.Tasks.Task.Delay(300);
        replacementTask.IsCompleted.Should().BeFalse("the replacement must wait for the in-flight reader to release the Context lock.");

        // Release the reader: it finishes against the pre-replacement
        // snapshot (consistent), then the replacement commits.
        releaseRead.SetResult();
        var result = await readerTask.WaitAsync(TimeSpan.FromSeconds(15));
        result.Blocks.Select(block => block.BlockId)
            .Should().Equal(["block-v1"], "the in-flight reader must observe the consistent pre-replacement snapshot.");

        await replacementTask.WaitAsync(TimeSpan.FromSeconds(15));
        var after = await _driver.ContextStore.GetCompressedContextAsync("tenant-a", contextId);
        after!.Blocks.Select(block => block.BlockId)
            .Should().Equal(["block-v2"], "the committed replacement must be observable afterwards.");
    }

    private static AgentCompressedContext Context(string tenantId, string contextId, string blockId)
        => new()
        {
            TenantId = tenantId,
            ContextId = contextId,
            Blocks =
            [
                new AgentCompressedContextBlock
                {
                    BlockId = blockId,
                    TenantId = tenantId,
                    Content = $"content-{blockId}",
                    CanonicalContentHash = CanonicalHashStub.For($"block-{blockId}"),
                    SourceRefs = [new AgentContextSourceRef
                    {
                        SourceKind = AgentSourceKind.ConversationTurn,
                        TenantId = tenantId,
                        SourceId = $"source-{blockId}"
                    }]
                }
            ]
        };

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerExceptions[0]
            : exception;

}
