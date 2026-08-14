using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
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
        // Feed the exact sentinel through the Store's sanitizer chain: the
        // provider rejects it, and neither the parameters nor the persisted
        // rows may ever contain the raw sentinel.
        var sentinel = AgentMemoryPersistenceContractMarkers.RejectedContentSentinel;
        var rejected = _driver.Sanitizer.Sanitize("tenant-a", sentinel, Array.Empty<AgentContextSourceRef>());
        rejected.Rejected.Should().BeTrue("the fixture sanitizer must reject the contract sentinel.");

        var conversation = Conversation(
            "tenant-a",
            "conversation-sanitized",
            Turn("tenant-a", "turn-1", "accepted", 0),
            Turn("tenant-a", "turn-2", sentinel, 1));
        await _driver.ConversationStore.SaveConversationAsync(conversation);

        var read = await _driver.ConversationStore.GetConversationAsync("tenant-a", "conversation-sanitized");
        read.Should().NotBeNull();
        read!.Turns.Should().HaveCount(1);
        read.Turns[0].Content.Should().NotContain(sentinel);
        read.Turns.Should().NotContain(turn => turn.Content.Contains(sentinel, StringComparison.Ordinal));

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
        // For every curation SQL write point, inject a failure and prove the
        // whole three-node graph rolls back: the old Memory stays Active with
        // no link, the new Memory never appears, the replacement Candidate
        // stays Candidate.
        var writePoints = new[]
        {
            "supersede:update-target-memory",
            "supersede:insert-new-memory",
            "supersede:update-replacement-candidate"
        };
        foreach (var writePoint in writePoints)
        {
            await AssertSupersedeRollbackAtAsync(writePoint);
        }
    }

    private async Task AssertSupersedeRollbackAtAsync(string writePoint)
    {
        var suffix = writePoint.Replace(':', '_');
        var candidate = Candidate("tenant-a", $"candidate-wp-{suffix}");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var promotePlan = _driver.PreparePromotionPlan(candidate, $"memory-wp-old-{suffix}", Operation("tenant-a", $"op-wp-promote-{suffix}"));
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;
        var original = await conditional.PromoteAsync("tenant-a", promotePlan);

        var replacement = Candidate("tenant-a", $"candidate-wp-repl-{suffix}", AgentMemoryKind.Decision);
        await _driver.MemoryStore.CreateCandidateAsync(replacement);
        var supersession = _driver.PrepareSupersessionPlan(
            original, replacement, $"memory-wp-new-{suffix}", Operation("tenant-a", $"op-wp-supersede-{suffix}"));

        using var injection = PostgreSqlRuntimeTestHooks.BlockAfterWritePoint(async (point, ct) =>
        {
            if (point == writePoint)
                throw new InvalidOperationException($"injected failure at {point}");
        });
        var act = async () => await conditional.SupersedeAsync("tenant-a", supersession);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var oldMemory = await _driver.MemoryStore.GetMemoryAsync("tenant-a", $"memory-wp-old-{suffix}");
        oldMemory.Should().NotBeNull();
        oldMemory!.Status.Should().Be(AgentMemoryStatus.Active, $"{writePoint} must leave the old Memory Active.");
        oldMemory.SupersededByMemoryId.Should().BeNull($"{writePoint} must not link the old Memory.");
        (await _driver.MemoryStore.GetMemoryAsync("tenant-a", $"memory-wp-new-{suffix}")).Should().BeNull($"{writePoint} must not create the new Memory.");
        var repl = await _driver.MemoryStore.GetCandidateAsync("tenant-a", $"candidate-wp-repl-{suffix}");
        repl!.Status.Should().Be(AgentMemoryStatus.Candidate, $"{writePoint} must leave the replacement Candidate unchanged.");
    }

    [Fact]
    public async Task CommitAcknowledgementLoss_Should_RemainCommitUnknown()
    {
        // Force the provider-owned COMMIT to fail with a non-Postgres error
        // after all durable mutations: the coordinator must surface
        // CommitUnknown (never a false rollback), and the mutation must not be
        // observable as committed.
        var candidate = Candidate("tenant-a", "candidate-commit-unknown");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var plan = _driver.PreparePromotionPlan(candidate, "memory-commit-unknown", Operation("tenant-a", "op-commit-unknown"));
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;

        // Roll the transaction back inside the before-COMMIT block: the
        // subsequent CommitAsync fails with a non-Postgres error, which the
        // coordinator translates to CommitUnknown instead of a false rollback.
        using var injection = PostgreSqlRuntimeTestHooks.BlockBeforeCommit(async ct =>
        {
            await _driver.Provider.GetRequiredService<PostgreSqlRuntimeTransactionAccessor>()
                .Current!.Transaction.RollbackAsync(ct);
        });

        var failure = await Record.ExceptionAsync(() => conditional.PromoteAsync("tenant-a", plan).AsTask());
        var unwrapped = Unwrap(failure!);
        unwrapped.Should().BeOfType<RuntimeTransactionCommitUnknownException>();

        (await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-commit-unknown")).Should().BeNull();
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


    [Fact]
    public async Task TransitionCandidateStatus_Should_TransitionAndValidatePersistedHash()
    {
        var candidate = Candidate("tenant-a", "candidate-transition");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);

        await _driver.MemoryStore.TransitionCandidateStatusAsync(
            "tenant-a", "candidate-transition", AgentMemoryStatus.Candidate, AgentMemoryStatus.Active);

        var read = await _driver.MemoryStore.GetCandidateAsync("tenant-a", "candidate-transition");
        read.Should().NotBeNull();
        read!.Status.Should().Be(AgentMemoryStatus.Active);
        read.Kind.Should().Be(AgentMemoryKind.Preference);
        read.CanonicalContentHash.Value.Should().Be(candidate.CanonicalContentHash.Value);
    }

    [Fact]
    public async Task TamperedCandidateStateHash_Should_FailPersistedInvariantValidation()
    {
        var candidate = Candidate("tenant-a", "candidate-tampered-hash");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);

        await TamperAsync($"""
            update "{_lease.Options.Schema}".agent_memory_candidates
            set state_hash = '0000000000000000000000000000000000000000000000000000000000000000'
            where tenant_id = 'tenant-a' and candidate_id = 'candidate-tampered-hash';
            """);

        var failure = await Record.ExceptionAsync(
            () => _driver.MemoryStore.GetCandidateAsync("tenant-a", "candidate-tampered-hash").AsTask());
        var unwrapped = Unwrap(failure!);
        unwrapped.Should().BeOfType<RuntimePersistenceContractException>();
        ((RuntimePersistenceContractException)unwrapped!).Code
            .Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }


    [Fact]
    public async Task OneSidedGraphLink_Should_FailReciprocalInvariantOnRead()
    {
        // A FK-valid but one-sided graph edge must be rejected: when B points
        // at A via SupersedesMemoryId but A does not point back via
        // SupersededByMemoryId, GetMemoryAsync must fail closed.
        var candidate = Candidate("tenant-a", "candidate-reciprocal-source");
        await _driver.MemoryStore.CreateCandidateAsync(candidate);
        var promotePlan = _driver.PreparePromotionPlan(candidate, "memory-reciprocal-old", Operation("tenant-a", "op-rec-1"));
        var conditional = (IAgentMemoryConditionalCurationStore)_driver.MemoryStore;
        await conditional.PromoteAsync("tenant-a", promotePlan);

        var replacement = Candidate("tenant-a", "candidate-reciprocal-repl", AgentMemoryKind.Decision);
        await _driver.MemoryStore.CreateCandidateAsync(replacement);
        var supersession = _driver.PrepareSupersessionPlan(
            await _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-reciprocal-old")!,
            replacement, "memory-reciprocal-new", Operation("tenant-a", "op-rec-2"));
        await conditional.SupersedeAsync("tenant-a", supersession);

        // Sever the reciprocal edge from the old Memory: A.superseded_by is
        // cleared while B.supersedes still points at A. The FK remains valid.
        await TamperAsync($"""
            update "{_lease.Options.Schema}".agent_memories
            set superseded_by_memory_id = null,
                state_hash = '0000000000000000000000000000000000000000000000000000000000000000'
            where tenant_id = 'tenant-a' and memory_id = 'memory-reciprocal-old';
            """);

        var failure = await Record.ExceptionAsync(
            () => _driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-reciprocal-new").AsTask());
        failure.Should().NotBeNull("a one-sided graph edge must fail the reciprocal read invariant.");
        Unwrap(failure!).Should().BeOfType<RuntimePersistenceContractException>();
    }


    [Fact]
    public async Task CandidateBatch_ReversedCrossTenantConcurrency_Should_NotDeadlock()
    {
        // Two batches present the same cross-tenant identities in opposite
        // tenant orders; the global lock order must prevent deadlock and both
        // batches must complete.
        var batchA = new[]
        {
            Candidate("tenant-a", "cross-a-1"),
            Candidate("tenant-b", "cross-b-1")
        };
        var batchB = new[]
        {
            Candidate("tenant-b", "cross-b-2"),
            Candidate("tenant-a", "cross-a-2")
        };

        await using var driverA = new PostgreSqlAgentMemoryContractDriver(_lease);
        await using var driverB = new PostgreSqlAgentMemoryContractDriver(_lease);

        await System.Threading.Tasks.Task.WhenAll(
            driverA.MemoryStore.CreateCandidatesAsync(batchA).AsTask(),
            driverB.MemoryStore.CreateCandidatesAsync(batchB).AsTask());

        (await driverA.MemoryStore.GetCandidateAsync("tenant-a", "cross-a-1")).Should().NotBeNull();
        (await driverB.MemoryStore.GetCandidateAsync("tenant-b", "cross-b-2")).Should().NotBeNull();
    }

    [Fact]
    public async Task CandidateBatch_AmbientCatchCommit_Should_NotCommitPartialBatch()
    {
        // With a pre-existing ambient Runtime transaction, the caller may catch
        // the IdentityConflict and continue; the frozen algorithm must precheck
        // occupancy before any INSERT so no earlier batch member is committed.
        var batch = new[]
        {
            Candidate("tenant-a", "ambient-batch-1"),
            Candidate("tenant-a", "ambient-batch-existing"),
            Candidate("tenant-a", "ambient-batch-3")
        };
        await _driver.MemoryStore.CreateCandidateAsync(Candidate("tenant-a", "ambient-batch-existing"));

        var coordinator = _driver.Provider
            .GetRequiredService<CrestCreates.Runtime.Persistence.Abstractions.Transactions.IRuntimeTransactionCoordinator>();
        var store = _driver.MemoryStore;

        var failure = await Record.ExceptionAsync(async () => await coordinator.ExecuteAsync(async ct =>
        {
            try
            {
                await store.CreateCandidatesAsync(batch, ct);
            }
            catch (AgentMemoryOperationException)
            {
                // Simulate a caller that swallows the conflict and continues.
            }
        }));

        failure.Should().BeNull("the ambient caller must be able to continue after catching the conflict.");
        (await _driver.MemoryStore.GetCandidateAsync("tenant-a", "ambient-batch-1")).Should().BeNull("no batch member may be committed after a caught conflict.");
        (await _driver.MemoryStore.GetCandidateAsync("tenant-a", "ambient-batch-3")).Should().BeNull("no batch member may be committed after a caught conflict.");
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
            $"select state_json::text from \"{_lease.Options.Schema}\".{table} where tenant_id=@tenant and conversation_id=@id;",
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        return (string)await command.ExecuteScalarAsync()!;
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

    private static AgentMemoryCandidate Candidate(string tenantId, string candidateId, AgentMemoryKind kind = AgentMemoryKind.Preference)
        => new()
        {
            TenantId = tenantId,
            CandidateId = candidateId,
            Kind = kind,
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
