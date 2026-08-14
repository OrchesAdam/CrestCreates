using System.Text.Json;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.Bootstrap;
using CrestCreates.Agent.Memory.Accountability.Production;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryAccountabilityCompositionTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task MemoryProducer_Should_PersistAcceptedDuplicateConflictAndTenantIsolation()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var logger = new RecordingLogger();
        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddAccountability()
            .AddSingleton<ILogger<AgentMemoryAccountabilityProducer>>(logger)
            .AddAgentMemoryAccountability()
            .BuildServiceProvider();

        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var identity = new AgentMemoryOperationIdentity { OperationId = "memory-pg-operation", OccurredAt = DateTimeOffset.UnixEpoch };
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = identity.OperationId,
            Result = "completed",
            EffectivePackHash = Hash("pack"),
            ReturnedCount = 1,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 1000,
            MinimumConfidence = "0.5"
        };
        var firstContext = Context("tenant-a", "cause-a", "parent-a", "inv-a");

        await producer.PublishRecallAsync(identity, firstContext, payload);
        (await CountAsync(lease.Options)).Should().Be(1);
        logger.Messages.Should().Contain(x => x.Contains("AGENT_MEMORY_ACCOUNTABILITY_RECORDED", StringComparison.Ordinal));

        await producer.PublishRecallAsync(identity, firstContext, payload);
        (await CountAsync(lease.Options)).Should().Be(1, "the same complete Memory fact must be Duplicate");
        logger.Messages.Should().Contain(x => x.Contains("AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE", StringComparison.Ordinal));

        await producer.PublishRecallAsync(identity, firstContext with { CausationId = "cause-b", ParentAuditId = "parent-b" }, payload);
        (await CountAsync(lease.Options)).Should().Be(1, "a changed Capability execution must be Conflict without replacing the first snapshot");
        logger.Messages.Should().Contain(x => x.Contains("AGENT_MEMORY_ACCOUNTABILITY_CONFLICT", StringComparison.Ordinal));
        var persisted = await ReadEnvelopeAsync(lease.Options);
        persisted.RootElement.GetProperty("causationId").GetString().Should().Be("cause-a");

        await producer.PublishRecallAsync(identity, Context("tenant-b", "cause-b", "parent-b", "inv-b"), payload);
        (await CountAsync(lease.Options)).Should().Be(2, "tenant identity is part of the durable audit identity");
    }

    [Fact]
    public async Task KnownCommitAndTypedConflictFacts_Should_RemainCorrectWithDurableStore()
    {
        // Full #56 composition: the real Promotion Service drives the durable
        // Store; the committed curation result is published as a durable
        // Accountability fact only after the provider-owned COMMIT returns.
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var logger = new RecordingLogger();
        using var provider = new ServiceCollection()
            .AddSingleton<ICanonicalHashComputer>(new LocalHashComputer())
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .AddAccountability()
            .AddSingleton<ILogger<AgentMemoryAccountabilityProducer>>(logger)
            .AddAgentMemoryAccountability()
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IAgentMemoryStore>();
        var promotion = provider.GetRequiredService<IAgentMemoryPromotionService>();
        var hashes = provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();

        var candidate = new AgentMemoryCandidate
        {
            TenantId = "tenant-a",
            CandidateId = "candidate-known",
            Kind = AgentMemoryKind.Decision,
            Content = "known committed content",
            CanonicalContentHash = hashes.ComputeContentHash("tenant-a", AgentSourceKind.ConversationTurn, "source-known", 0, 0, "known committed content"),
            Confidence = AgentMemoryConfidence.High
        };
        await store.CreateCandidateAsync(candidate);

        var operation = new AgentMemoryOperationRequest
        {
            TenantId = "tenant-a",
            InvocationContext = Context("tenant-a", "cause-known", "parent-known", "inv-known"),
            Reason = "known composition",
            Identity = new AgentMemoryOperationIdentity { OperationId = "memory-pg-known", OccurredAt = DateTimeOffset.UnixEpoch },
            Explanation = "known composition explanation"
        };

        await promotion.PromoteAsync("tenant-a", "candidate-known", "memory-known", operation);
        (await CountAsync(lease.Options)).Should().Be(1, "a known committed Promote must persist exactly one durable fact.");
        logger.Messages.Should().Contain(x => x.Contains("AGENT_MEMORY_ACCOUNTABILITY_RECORDED", StringComparison.Ordinal));

        // A typed conflict (occupied identity) must not replace the committed fact.
        var second = new AgentMemoryCandidate
        {
            TenantId = "tenant-a",
            CandidateId = "candidate-known-second",
            Kind = AgentMemoryKind.Decision,
            Content = "second content",
            CanonicalContentHash = hashes.ComputeContentHash("tenant-a", AgentSourceKind.ConversationTurn, "source-known-2", 0, 0, "second content"),
            Confidence = AgentMemoryConfidence.High
        };
        await store.CreateCandidateAsync(second);
        var conflict = await Record.ExceptionAsync(() => promotion.PromoteAsync(
            "tenant-a", "candidate-known-second", "memory-known",
            operation with { Identity = new AgentMemoryOperationIdentity { OperationId = "memory-pg-known-conflict", OccurredAt = DateTimeOffset.UnixEpoch } }).AsTask());
        conflict.Should().NotBeNull("occupied identity must fail the second Promote.");
        (await CountAsync(lease.Options)).Should().Be(1, "a conflict must not manufacture an additional deterministic fact.");
    }

    [Fact]
    public async Task UnavailableOrCommitUnknown_Should_CreateNoFalseDeterministicCurationFact()
    {
        // A provider-level unavailable path must not manufacture a deterministic
        // curation failure fact: the Store call fails before any durable
        // mutation, so no fact row and no recorded diagnostic may appear.
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var logger = new RecordingLogger();
        var unavailable = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Timeout=1;Pooling=false",
            Schema = "unavailable_schema"
        };
        using var provider = new ServiceCollection()
            .AddSingleton<ICanonicalHashComputer>(new LocalHashComputer())
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(unavailable)
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .AddAccountability()
            .AddSingleton<ILogger<AgentMemoryAccountabilityProducer>>(logger)
            .AddAgentMemoryAccountability()
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IAgentMemoryStore>();
        var promotion = provider.GetRequiredService<IAgentMemoryPromotionService>();
        var hashes = provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();

        var candidate = new AgentMemoryCandidate
        {
            TenantId = "tenant-a",
            CandidateId = "candidate-unavailable",
            Kind = AgentMemoryKind.Decision,
            Content = "unavailable content",
            CanonicalContentHash = hashes.ComputeContentHash("tenant-a", AgentSourceKind.ConversationTurn, "source-unavailable", 0, 0, "unavailable content"),
            Confidence = AgentMemoryConfidence.High
        };

        var failure = await Record.ExceptionAsync(() => store.CreateCandidateAsync(candidate).AsTask());
        failure.Should().NotBeNull("an unreachable backend must fail the Candidate write.");
        failure.Should().BeOfType<RuntimePersistenceUnavailableException>();

        var operation = new AgentMemoryOperationRequest
        {
            TenantId = "tenant-a",
            InvocationContext = Context("tenant-a", "cause-unavailable", "parent-unavailable", "inv-unavailable"),
            Reason = "unavailable composition",
            Identity = new AgentMemoryOperationIdentity { OperationId = "memory-pg-unavailable", OccurredAt = DateTimeOffset.UnixEpoch },
            Explanation = "unavailable composition explanation"
        };
        var promoteFailure = await Record.ExceptionAsync(() => promotion.PromoteAsync(
            "tenant-a", "candidate-unavailable", "memory-unavailable", operation).AsTask());
        promoteFailure.Should().NotBeNull("unavailability must fail the formal Promote too.");

        (await CountAsync(lease.Options)).Should().Be(0, "an unavailable path must never persist a deterministic curation fact.");
        logger.Messages.Should().NotContain(x => x.Contains("AGENT_MEMORY_ACCOUNTABILITY_RECORDED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommittedAccountability_Should_Never_Precede_DurableCommit()
    {
        // Block the provider-owned COMMIT of a real Promote: while the durable
        // Store is still in flight, no Accountability fact may exist. Only
        // after the COMMIT completes does the service publish the fact.
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var logger = new RecordingLogger();
        using var provider = new ServiceCollection()
            .AddSingleton<ICanonicalHashComputer>(new LocalHashComputer())
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .AddAccountability()
            .AddSingleton<ILogger<AgentMemoryAccountabilityProducer>>(logger)
            .AddAgentMemoryAccountability()
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IAgentMemoryStore>();
        var promotion = provider.GetRequiredService<IAgentMemoryPromotionService>();
        var hashes = provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();

        var candidate = new AgentMemoryCandidate
        {
            TenantId = "tenant-a",
            CandidateId = "candidate-committed",
            Kind = AgentMemoryKind.Decision,
            Content = "committed content",
            CanonicalContentHash = hashes.ComputeContentHash("tenant-a", AgentSourceKind.ConversationTurn, "source-committed", 0, 0, "committed content"),
            Confidence = AgentMemoryConfidence.High
        };
        await store.CreateCandidateAsync(candidate);

        var operation = new AgentMemoryOperationRequest
        {
            TenantId = "tenant-a",
            InvocationContext = Context("tenant-a", "cause-committed", "parent-committed", "inv-committed"),
            Reason = "committed composition",
            Identity = new AgentMemoryOperationIdentity { OperationId = "memory-pg-committed", OccurredAt = DateTimeOffset.UnixEpoch },
            Explanation = "committed composition explanation"
        };

        using var gate = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);
        using var injection = PostgreSqlRuntimeTestHooks.BlockBeforeCommit(async ct =>
        {
            // The Store has done its durable mutations but not yet committed:
            // no fact may exist in this window.
            (await CountAsync(lease.Options)).Should().Be(0, "no Accountability fact may precede the durable COMMIT.");
            gate.Release();
            await release.WaitAsync(ct);
        });

        var promote = promotion.PromoteAsync("tenant-a", "candidate-committed", "memory-committed", operation).AsTask();
        await gate.WaitAsync(TimeSpan.FromSeconds(30));
        (await CountAsync(lease.Options)).Should().Be(0, "the fact must still be absent while the COMMIT is pending.");

        // Release the COMMIT; the durable Store call returns, and only then
        // does the service publish the committed curation fact.
        release.Release();
        await promote.WaitAsync(TimeSpan.FromSeconds(30));
        (await CountAsync(lease.Options)).Should().Be(1, "the committed curation fact must be durable after the COMMIT.");
        logger.Messages.Should().Contain(x => x.Contains("AGENT_MEMORY_ACCOUNTABILITY_RECORDED", StringComparison.Ordinal));
    }


    private static AgentMemoryInvocationContext Context(string tenant, string causation, string parent, string invocation)
        => new()
        {
            TenantId = tenant,
            ActorId = "agent-1",
            ActorKind = "agent",
            AgentId = "agent-1",
            SessionId = "session-1",
            InvocationId = invocation,
            CorrelationId = "correlation-1",
            CausationId = causation,
            ParentAuditId = parent,
            InvocationSource = "agent"
        };

    private static CanonicalHash Hash(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = AgentMemoryAccountabilityPayloadKinds.EffectivePackArtifactKind,
            Scope = AgentMemoryAccountabilityPayloadKinds.EffectivePackScope,
            Purpose = AgentMemoryAccountabilityPayloadKinds.EffectivePackPurpose,
            ContractVersion = AgentMemoryAccountabilityPayloadKinds.EffectivePackContractVersion,
            CanonicalShapeVersion = AgentMemoryAccountabilityPayloadKinds.EffectivePackCanonicalShapeVersion
        };

    private async Task<long> CountAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select count(*) from \"{options.Schema}\".runtime_audit_envelopes where sink_id=@sink;", connection);
        command.Parameters.AddWithValue("sink", "postgresql-runtime-audit");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<JsonDocument> ReadEnvelopeAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select envelope_json::text from \"{options.Schema}\".runtime_audit_envelopes where sink_id=@sink limit 1;", connection);
        command.Parameters.AddWithValue("sink", "postgresql-runtime-audit");
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
    }

    private sealed class RecordingLogger : ILogger<AgentMemoryAccountabilityProducer>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Messages.Add(formatter(state, exception));
        }
    }

    private sealed class LocalHashComputer : ICanonicalHashComputer
    {
        public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Hash($"{descriptor.GetType().Name}-contract");

        public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Hash($"{descriptor.GetType().Name}-definition");

        public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
            => Hash(projection.Metadata.ArtifactKind + "-" + projection.Metadata.Purpose);

        private static CanonicalHash Hash(string value)
            => new()
            {
                Value = value,
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "AgentMemoryComposition",
                Scope = "InternalFull",
                Purpose = "composition",
                ContractVersion = "memory-hash-v1",
                CanonicalShapeVersion = "composition-v1"
            };
    }
}
