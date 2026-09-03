using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Message;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Runtime.Persistence.Testing.Cases;
using CrestCreates.Runtime.Persistence.Testing.Evidence;
using CrestCreates.Runtime.Persistence.Testing.Manifest;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.InMemory.Tests;

public sealed class InMemoryOutboxDispatchTests
{
    [Fact]
    public async Task Append_rejects_manually_constructed_high_precision_message_before_mutation()
    {
        var services = new ServiceCollection();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var timestamp = DateTimeOffset.UnixEpoch.AddTicks(1);
        var metadata = new OutboxMessageMetadata
        {
            MessageId = "precision-manual",
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

        var action = () => provider.GetRequiredService<InMemoryRuntimeTransactionCoordinator>().ExecuteAsync(
            async ct => await provider.GetRequiredService<ITransactionalOutboxWriter>().AppendAsync(message, ct)).AsTask();
        await action.Should().ThrowAsync<CrestCreates.Runtime.Persistence.Abstractions.Errors.RuntimePersistenceContractException>();

        var claims = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "precision-manual-test",
            BatchSize = 10,
            SupportedContractIds = new HashSet<string>(["contract/v1"], StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        });
        claims.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedOutboxContract_UsesProviderClockAndEmptyClaim()
    {
        var services = new ServiceCollection();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        await OutboxDispatchContractCases.EmptyClaimUsesProviderClockAsync(
            provider.GetRequiredService<IOutboxDispatchStore>());
    }

    [Fact]
    public async Task Append_claim_and_stale_lease_are_fenced()
    {
        var services = new ServiceCollection();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<InMemoryRuntimeTransactionCoordinator>();
        var writer = provider.GetRequiredService<ITransactionalOutboxWriter>();
        var dispatch = provider.GetRequiredService<IOutboxDispatchStore>();
        var factory = new DefaultOutboxMessageFactory();
        var message = factory.Create("evt-1", "tenant-a", "contract/v1", "payload/v1", [1, 2, 3], []);

        await coordinator.ExecuteAsync(async ct =>
        {
            (await writer.AppendAsync(message, ct)).Should().Be(OutboxAppendResult.Appended);
        });

        var first = (await dispatch.ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "worker-a", BatchSize = 1, LeaseDuration = TimeSpan.FromMinutes(1),
            Now = DateTimeOffset.UtcNow
        })).Single();

        (await dispatch.AckAsync(message.Metadata.MessageId,
            new OutboxDeliveryLease { OwnerId = "worker-b", Fence = first.Lease.Fence, Attempt = first.Lease.Attempt, ExpiresAt = first.Lease.ExpiresAt }))
            .Should().Be(OutboxDeliveryMutationResult.StaleLease);

        (await dispatch.AckAsync(message.Metadata.MessageId, first.Lease))
            .Should().Be(OutboxDeliveryMutationResult.Applied);
    }

    [Fact]
    public async Task Claim_rejects_unsupported_active_composition_without_mutating_the_row()
    {
        var services = new ServiceCollection();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<InMemoryRuntimeTransactionCoordinator>();
        var writer = provider.GetRequiredService<ITransactionalOutboxWriter>();
        var dispatch = provider.GetRequiredService<IOutboxDispatchStore>();
        var message = new DefaultOutboxMessageFactory().Create("evt-composition", "tenant-a", "contract/v1", "payload/v1", [1, 2, 3]);

        await coordinator.ExecuteAsync(async ct => await writer.AppendAsync(message, ct));

        var action = () => dispatch.ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "worker-a",
            SupportedContractIds = new HashSet<string>(StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        }).AsTask();
        await action.Should().ThrowAsync<OutboxCompositionException>();

        var claim = (await dispatch.ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = "worker-a",
            SupportedContractIds = new HashSet<string>(["contract/v1"], StringComparer.Ordinal),
            SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
        })).Single();
        claim.Lease.Attempt.Should().Be(1);
    }

    [Fact]
    public async Task Terminal_mutation_replay_is_exact_fence_idempotent_and_stale_fenced()
    {
        var services = new ServiceCollection();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<InMemoryRuntimeTransactionCoordinator>();
        var writer = provider.GetRequiredService<ITransactionalOutboxWriter>();
        var dispatch = provider.GetRequiredService<IOutboxDispatchStore>();
        var message = new DefaultOutboxMessageFactory().Create("evt-terminal", "tenant-a", "contract/v1", "payload/v1", [1]);
        await coordinator.ExecuteAsync(async ct => await writer.AppendAsync(message, ct));
        var claim = (await dispatch.ClaimAsync(new OutboxClaimRequest { OwnerId = "worker-a", BatchSize = 1 })).Single();

        var applied = await dispatch.AckAsync(message.Metadata.MessageId, claim.Lease);
        applied.Should().Be(OutboxDeliveryMutationResult.Applied);
        Phase9cEvidenceProducer.RecordAfterAssertion(
            Tuple("Valid_Owner_Should_Acknowledge_To_Delivered", "L09"),
            $"{nameof(InMemoryOutboxDispatchTests)}.{nameof(Terminal_mutation_replay_is_exact_fence_idempotent_and_stale_fenced)}:Applied");

        var replay = await dispatch.AckAsync(message.Metadata.MessageId, claim.Lease);
        replay.Should().Be(OutboxDeliveryMutationResult.AlreadyApplied);
        Phase9cEvidenceProducer.RecordAfterAssertion(
            Tuple("Ack_Replay_With_ExactTerminalFence_Should_Be_AlreadyApplied", "L14"),
            $"{nameof(InMemoryOutboxDispatchTests)}.{nameof(Terminal_mutation_replay_is_exact_fence_idempotent_and_stale_fenced)}:AlreadyApplied");
        Phase9cEvidenceProducer.RecordAfterAssertion(
            Tuple("AlreadyApplied_Should_Not_Reopen_TerminalState", "L14"),
            $"{nameof(InMemoryOutboxDispatchTests)}.{nameof(Terminal_mutation_replay_is_exact_fence_idempotent_and_stale_fenced)}:TerminalState");

        var stale = await dispatch.DeadLetterAsync(message.Metadata.MessageId,
            claim.Lease with { Fence = claim.Lease.Fence + 1 },
            new OutboxDeliveryFailure { Code = "different", Message = "different terminal outcome" });
        stale.Should().Be(OutboxDeliveryMutationResult.StaleFence);
        Phase9cEvidenceProducer.RecordAfterAssertion(
            Tuple("TerminalReplay_With_DifferentFence_Should_Be_StaleOrConflict", "L16"),
            $"{nameof(InMemoryOutboxDispatchTests)}.{nameof(Terminal_mutation_replay_is_exact_fence_idempotent_and_stale_fenced)}:StaleFence");
    }

    private static Phase9cEvidenceTuple Tuple(string acceptanceName, string caseId)
        => Phase9cEvidenceRunnerCatalog.ForAcceptance(acceptanceName)
            .Single(tuple => tuple.CaseId == caseId && tuple.Runner == "IM");
}
