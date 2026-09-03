using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Message;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.Testing.Cases;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlOutboxDispatchTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task Append_rejects_manually_constructed_high_precision_message_before_provider_mutation()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .BuildServiceProvider();
        var timestamp = DateTimeOffset.UnixEpoch.AddTicks(1);
        var metadata = new OutboxMessageMetadata
        {
            MessageId = "precision-manual-pg",
            TenantId = "tenant-a",
            ContractId = "contract/v1",
            EventName = "contract/v1",
            RequiredConsumerIds = [],
            CreatedAt = timestamp,
            OccurredAt = timestamp
        };
        var payload = new byte[] { 1 };
        var message = new OutboxMessage
        {
            Metadata = metadata,
            Payload = payload,
            Integrity = OutboxMessageIntegrity.Compute(metadata, payload)
        };

        var action = () => provider.GetRequiredService<IRuntimeTransactionCoordinator>().ExecuteAsync(
            async ct => await provider.GetRequiredService<ITransactionalOutboxWriter>().AppendAsync(message, ct)).AsTask();
        await action.Should().ThrowAsync<RuntimePersistenceContractException>();

        var claims = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "precision-manual-pg-test",
            BatchSize = 10,
            SupportedContractIds = new HashSet<string>(["contract/v1"], StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        });
        claims.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedOutboxContract_UsesProviderClockAndEmptyClaim()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = new NpgsqlSlimDataSourceBuilder(fixture.ConnectionString)
            .EnableArrays()
            .Build();
        await OutboxDispatchContractCases.EmptyClaimUsesProviderClockAsync(
            new PostgreSqlOutboxDispatchStore(lease.Options, dataSource));
    }

    [Fact]
    public async Task OutboxV1Integrity_SurvivesPostgreSqlRoundTripWithHundredNanosecondTail()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .BuildServiceProvider();
        var timestamp = DateTimeOffset.UnixEpoch.AddTicks(1_234_567);
        var message = new DefaultOutboxMessageFactory().Create(
            "precision-roundtrip",
            "tenant-a",
            "test.contract/v1",
            "test.payload/v1",
            new byte[] { 1, 2, 3 },
            createdAt: timestamp);

        await provider.GetRequiredService<IRuntimeTransactionCoordinator>().ExecuteAsync(async ct =>
        {
            (await provider.GetRequiredService<ITransactionalOutboxWriter>().AppendAsync(message, ct))
                .Should().Be(OutboxAppendResult.Appended);
        });

        await using var dataSource = new NpgsqlSlimDataSourceBuilder(lease.Options.ConnectionString)
            .EnableArrays()
            .Build();
        var claims = await new PostgreSqlOutboxDispatchStore(lease.Options, dataSource).ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "precision-test",
            BatchSize = 1,
            LeaseDuration = TimeSpan.FromMinutes(1),
            SupportedContractIds = new HashSet<string>(["test.contract/v1"], StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        });

        claims.Should().ContainSingle();
        var roundTripped = claims[0].Message;
        roundTripped.Metadata.OccurredAt.Should().Be(message.Metadata.OccurredAt);
        OutboxMessageIntegrity.Matches(roundTripped).Should().BeTrue();
        roundTripped.Integrity.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        roundTripped.Integrity.CanonicalShapeVersion.Should().Be("runtime-outbox-message-v1");
    }

    [Fact]
    public async Task DeliveredStateCheckRejectsAnActiveLease()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var action = async () =>
        {
            await using var command = new NpgsqlCommand($"""
                insert into "{lease.Options.Schema}".runtime_outbox_messages
                    (message_id, contract_id, event_name, event_version, tenant_scope_kind, tenant_id,
                     occurred_at, required_consumer_ids_json, payload_utf8, integrity_json, created_at, available_at,
                     updated_at, status, lease_owner_id, lease_expires_at)
                values ('retry-invalid', 'test.contract', 'test.event', 1, 'host', '', clock_timestamp(), '[]'::jsonb,
                        decode('01', 'hex'), @integrity::jsonb,
                        clock_timestamp(), clock_timestamp(), clock_timestamp(), 2, 'owner', clock_timestamp());
                """, connection);
            command.Parameters.AddWithValue("integrity", IntegrityJson);
            await command.ExecuteNonQueryAsync();
        };

        await action.Should().ThrowAsync<PostgresException>()
            .Where(exception => exception.ConstraintName == "ck_runtime_outbox_delivered_state");
    }

    [Fact]
    public async Task ClaimUsesAvailableOccurredAndMessageIdOrder()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var availableAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        foreach (var row in new[]
        {
            ("message-c", DateTimeOffset.UtcNow.AddMinutes(-3), availableAt.AddSeconds(30)),
            ("message-a", DateTimeOffset.UtcNow.AddMinutes(-2), availableAt),
            ("message-b", DateTimeOffset.UtcNow.AddMinutes(-2), availableAt)
        })
        {
            await InsertMessageAsync(connection, lease.Options.Schema, row.Item1, row.Item2, row.Item3);
        }

        await using var dataSource = new NpgsqlSlimDataSourceBuilder(fixture.ConnectionString)
            .EnableArrays()
            .Build();
        var store = new PostgreSqlOutboxDispatchStore(lease.Options, dataSource);
        var claims = await store.ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "claim-order-test",
            BatchSize = 3,
            LeaseDuration = TimeSpan.FromMinutes(1),
            SupportedContractIds = new HashSet<string>(["test.contract"], StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        });

        claims.Select(claim => claim.Message.Metadata.MessageId)
            .Should().Equal("message-a", "message-b", "message-c");
    }

    [Fact]
    public async Task LegacyCompletionDispatchFailedBlocksProviderPreflight()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"""
            insert into "{lease.Options.Schema}".runtime_human_task_instances
                (tenant_scope_kind, tenant_id, instance_id, revision, status, human_task_pin_json,
                 state_json, required_consumer_ids_json, completed_at)
            values ('host', '', 'legacy-task', 1, 4, 'null'::jsonb, 'null'::jsonb, '[]'::jsonb, clock_timestamp());
            """, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await using var dataSource = new NpgsqlSlimDataSourceBuilder(fixture.ConnectionString)
            .EnableArrays()
            .Build();
        var preflight = new PostgreSqlHumanTaskCompletionObligationPreflight(lease.Options, dataSource);
        var action = () => preflight.ValidateAsync([], new HashSet<string>(StringComparer.Ordinal)).AsTask();

        var exception = await action.Should().ThrowAsync<RuntimePersistenceContractException>();
        exception.Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
        exception.Which.Message.Should().Contain("legacy-task");
    }

    private static async Task InsertMessageAsync(NpgsqlConnection connection, string schema, string messageId, DateTimeOffset occurredAt, DateTimeOffset availableAt)
    {
        await using var command = new NpgsqlCommand($"""
            insert into "{schema}".runtime_outbox_messages
                (message_id, contract_id, event_name, event_version, tenant_scope_kind, tenant_id,
                 occurred_at, required_consumer_ids_json, payload_utf8, integrity_json, created_at, available_at, updated_at)
            values (@id, 'test.contract', 'test.event', 1, 'host', '', @occurred, '[]'::jsonb, decode('01', 'hex'),
                    @integrity::jsonb,
                    @occurred, @available, clock_timestamp());
            """, connection);
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("integrity", IntegrityJson);
        command.Parameters.AddWithValue("occurred", occurredAt);
        command.Parameters.AddWithValue("available", availableAt);
        await command.ExecuteNonQueryAsync();
    }

    private const string IntegrityJson = "{\"value\":\"x\",\"algorithm\":\"SHA-256\",\"algorithmVersion\":\"v1\",\"artifactKind\":\"Event\",\"scope\":\"InternalFull\",\"purpose\":\"Integrity\",\"contractVersion\":\"v1\",\"canonicalShapeVersion\":\"v1\"}";
}
