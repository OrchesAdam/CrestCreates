using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Message;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.InMemory.Tests;

public sealed class InMemoryOutboxDispatchTests
{
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
}
