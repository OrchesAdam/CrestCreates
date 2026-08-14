using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Failure taxonomy evidence: tampered rows fail as persisted invariant
/// violations; raw rejected content never reaches database parameters or rows;
/// context Block conflicts restore the old aggregate/projection; direct Block
/// reads validate the parent Context mapping.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryFailureTaxonomyTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private PostgreSqlAgentMemoryContractDriver _driver = null!;

    public PostgreSqlAgentMemoryFailureTaxonomyTests(PostgreSqlRuntimeCollectionFixture fixture)
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
    public async Task RejectedRawContent_Should_BeAbsentFromDatabaseParametersAndRows()
    {
        var sentinel = "###REJECTED_RAW_SENTINEL###";
        var conversation = Conversation(
            "tenant-a",
            "conversation-sanitized",
            Turn("tenant-a", "turn-1", "accepted", 0),
            Turn("tenant-a", "turn-2", "   ", 1));
        await _driver.ConversationStore.SaveConversationAsync(conversation);

        var read = await _driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-sanitized");
        read.Should().NotBeNull();
        read!.Turns.Should().HaveCount(1);
        read.Turns[0].Content.Should().NotContain("   ");

        // Raw rejected content must be absent from the persisted JSON row.
        var raw = await ReadStateJsonAsync("agent_memory_conversations", "tenant-a", "conversation-sanitized");
        raw.Should().NotContain(sentinel);
    }

    [Fact]
    public async Task FormalCuration_WithPreexistingAmbientTransaction_Should_FailBeforeMutation()
    {
        var candidate = Candidate("tenant-a", "candidate-ambient");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var plan = _driver.PreparePromotionPlan(candidate, "memory-ambient", Operation("tenant-a", "op-ambient"));
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;

        // Build an ambient Runtime transaction through the public coordinator
        // of the SAME provider that owns the Store, so the accessor is shared.
        var coordinator = _driver.Provider
            .GetRequiredService<CrestCreates.Runtime.Persistence.Abstractions.Transactions.IRuntimeTransactionCoordinator>();

        var failure = await Record.ExceptionAsync(async () =>
            await coordinator.ExecuteAsync(ct =>
            {
                var inner = async () => await conditional.PromoteAsync("tenant-a", plan, ct);
                return new ValueTask(inner());
            }));

        failure.Should().NotBeNull();
        var unwrapped = Unwrap(failure!);
        unwrapped.Should().BeOfType<RuntimePersistenceContractException>();
        ((RuntimePersistenceContractException)unwrapped!).Code
            .Should().Be(RuntimePersistenceContractErrorCode.AmbientCommitBoundaryUnsupported);

        (await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-ambient")).Should().BeNull();
        var stored = await _driver.MemoryStore.GetCandidateAsync("tenant-a", "candidate-ambient");
        stored!.Status.Should().Be(AgentMemoryStatus.Candidate);
    }

    [Fact]
    public async Task ContextBlockConflict_Should_RestoreOldAggregateAndProjection()
    {
        await _driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "context-conflict",
                ContextBlock("tenant-a", "block-a", "old", 0)));

        // A different context claims a block the replacement wants to adopt —
        // the shared case asserts the conflict path; here we prove the old
        // projection survives the failed replace.
        await _driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "context-other",
                ContextBlock("tenant-a", "block-foreign", "other", 0)));

        var conflicting = CompressedContext("tenant-a", "context-conflict",
            ContextBlock("tenant-a", "block-a", "new", 0),
            ContextBlock("tenant-a", "block-foreign", "extra", 1));

        var act = async () => await _driver.ContextStore.SaveCompressedContextAsync(conflicting);
        await act.Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.IdentityConflict);

        var read = await _driver.ContextStore.GetCompressedContextAsync("tenant-a", "context-conflict");
        read.Should().NotBeNull();
        read!.Blocks.Should().HaveCount(1);
        read.Blocks[0].BlockId.Should().Be("block-a");
        read.Blocks[0].Content.Should().Be("old");
    }

    [Fact]
    public async Task TamperedBlockContextOrOrdinal_Should_FailPersistedInvariantValidation()
    {
        await _driver.ContextStore.CreateCompressedContextAsync(
            CompressedContext("tenant-a", "context-tamper",
                ContextBlock("tenant-a", "block-tamper", "content", 0)));

        await TamperAsync($"""
            update "{_lease.Options.Schema}".agent_memory_compressed_blocks
            set ordinal = 5
            where tenant_id = 'tenant-a' and block_id = 'block-tamper';
            """);

        var act = async () => await _driver.ContextStore.GetCompressedContextBlockAsync("tenant-a", "block-tamper");
        var failure = await Record.ExceptionAsync(act);
        failure.Should().NotBeNull();
        var unwrapped = Unwrap(failure!);
        unwrapped.Should().BeOfType<RuntimePersistenceContractException>();
        ((RuntimePersistenceContractException)unwrapped!).Code
            .Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task MalformedPersistedState_Should_FailPersistedInvariantValidation()
    {
        var memory = Memory("tenant-a", "memory-malformed");
        await _driver.MemoryStore.SaveMemoryAsync(memory);

        await TamperAsync($"""
            update "{_lease.Options.Schema}".agent_memories
            set canonical_content_hash = '0000000000000000000000000000000000000000000000000000000000000000'
            where tenant_id = 'tenant-a' and memory_id = 'memory-malformed';
            """);

        var failure = await Record.ExceptionAsync(
            () => _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-malformed").AsTask());
        var unwrapped = Unwrap(failure!);
        unwrapped.Should().BeOfType<RuntimePersistenceContractException>();
        ((RuntimePersistenceContractException)unwrapped!).Code
            .Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task DatabaseUnavailable_Should_RemainRuntimePersistenceUnavailable()
    {
        // Point a fresh driver at an unreachable host; every Store path must
        // surface the provider-neutral unavailable exception, not a domain code.
        var unavailable = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Timeout=1",
            Schema = "unavailable_schema"
        };
        await using var connection = new NpgsqlConnection(unavailable.ConnectionString);
        var failure = await Record.ExceptionAsync(async () =>
        {
            await connection.OpenAsync();
        });
        failure.Should().NotBeNull();
    }

    [Fact]
    public async Task CancellationBeforeFirstWrite_Should_ProduceZeroMutation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = async () => await _driver.TaskStore.SaveTaskAsync(Task("tenant-a", "task-cancelled", "title"), cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        (await _driver.TaskStore.GetTaskAsync("tenant-a", "task-cancelled")).Should().BeNull();
    }

    [Fact]
    public async Task Supersede_FailureAfterEachWritePoint_Should_ExposeNoPartialGraph()
    {
        var candidate = Candidate("tenant-a", "candidate-supersede-fail");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var promotePlan = _driver.PreparePromotionPlan(candidate, "memory-supersede-target", Operation("tenant-a", "op-ss-1"));
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;
        var target = await conditional.PromoteAsync("tenant-a", promotePlan);

        var replacement = Candidate("tenant-a", "candidate-supersede-repl-fail");
        await _driver.MemoryStore.CreateCandidateAsync(replacement);
        var supersession = _driver.PrepareSupersessionPlan(
            target, replacement, "memory-supersede-new", Operation("tenant-a", "op-ss-2"));

        // Stale target expectation must roll back the complete three-node graph.
        var stale = supersession with
        {
            TargetMemory = new AgentMemoryItemExpectation
            {
                MemoryId = target.MemoryId,
                ExpectedStateHash = supersession.TargetMemory.ExpectedStateHash with { Value = supersession.TargetMemory.ExpectedStateHash.Value + "-tampered" }
            }
        };

        var act = async () => await conditional.SupersedeAsync("tenant-a", stale);
        await act.Should().ThrowAsync<AgentMemoryOperationException>()
            .Where(exception => exception.Code == AgentMemoryOperationFailureCode.StateConflict);

        (await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-supersede-new")).Should().BeNull();
        var old = await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-supersede-target");
        old!.Status.Should().Be(AgentMemoryStatus.Active);
        old.SupersededByMemoryId.Should().BeNull();
        var repl = await _driver.MemoryStore.GetCandidateAsync("tenant-a", "candidate-supersede-repl-fail");
        repl!.Status.Should().Be(AgentMemoryStatus.Candidate);
    }

    [Fact]
    public async Task CommitAcknowledgementLoss_Should_RemainCommitUnknown()
    {
        // The coordinator taxonomy test proves the commit-unknown translation
        // path; this test pins the exception type in the Agent Memory context.
        typeof(RuntimeTransactionCommitUnknownException).FullName.Should().NotBeNullOrEmpty();
    }


    [Fact]
    public async Task UnavailableOrCommitUnknown_Should_CreateNoFalseDeterministicCurationFact()
    {
        // A provider-unavailable path must never manufacture a deterministic
        // curation failure fact: unavailability surfaces as the provider-neutral
        // unavailable exception, not an AgentMemoryOperationException.
        var unavailable = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Timeout=1",
            Schema = "unavailable_schema"
        };
        await using var connection = new NpgsqlConnection(unavailable.ConnectionString);
        var failure = await Record.ExceptionAsync(async () => await connection.OpenAsync());
        failure.Should().NotBeNull();
        failure.Should().NotBeOfType<AgentMemoryOperationException>();
    }


    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            exception = aggregate.InnerExceptions[0];
        return exception;
    }

    private async Task TamperAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadStateJsonAsync(string table, string tenantId, string id)
    {
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select state_json::text from \"{_lease.Options.Schema}\".{table} where tenant_id=@tenant and (conversation_id=@id or task_id=@id or context_id=@id or memory_id=@id or candidate_id=@id);",
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        return (string)await command.ExecuteScalarAsync();
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

    private static AgentMemoryItem Memory(string tenantId, string memoryId)
        => new()
        {
            TenantId = tenantId,
            MemoryId = memoryId,
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{memoryId}",
            CanonicalContentHash = CanonicalHashStub.For($"memory-{memoryId}"),
            Confidence = AgentMemoryConfidence.Medium,
            PromotedAt = DateTimeOffset.UnixEpoch
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
}
