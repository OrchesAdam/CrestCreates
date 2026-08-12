using System.Text.Json;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.Bootstrap;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddAccountability()
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

        await producer.PublishRecallAsync(identity, firstContext, payload);
        (await CountAsync(lease.Options)).Should().Be(1, "the same complete Memory fact must be Duplicate");

        await producer.PublishRecallAsync(identity, firstContext with { CausationId = "cause-b", ParentAuditId = "parent-b" }, payload);
        (await CountAsync(lease.Options)).Should().Be(1, "a changed Capability execution must be Conflict without replacing the first snapshot");
        var persisted = await ReadEnvelopeAsync(lease.Options);
        persisted.RootElement.GetProperty("causationId").GetString().Should().Be("cause-a");

        await producer.PublishRecallAsync(identity, Context("tenant-b", "cause-b", "parent-b", "inv-b"), payload);
        (await CountAsync(lease.Options)).Should().Be(2, "tenant identity is part of the durable audit identity");
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
            AlgorithmVersion = "v1",
            ArtifactKind = "AgentMemoryEffectivePack",
            Scope = "TenantVisible",
            Purpose = "Accountability",
            ContractVersion = "v1",
            CanonicalShapeVersion = "v1"
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
}
