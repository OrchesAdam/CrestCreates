using System.Diagnostics;
using System.Text.Json;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public sealed class OptionalLocalEventCompatibilityTests
{
    [Fact]
    public async Task NonCooperativeOptionalHandler_ShouldNotHoldOutboxHandlerOpen()
    {
        var services = new ServiceCollection();
        services.AddHumanTaskRuntime();
        services.AddScoped<ILocalEventBus, NonCooperativeLocalEventBus>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var registration = scope.ServiceProvider
            .GetRequiredService<IEnumerable<OutboxDeliveryHandlerRegistration>>()
            .Single(item => item.ContractId == HumanTaskDeliveryConstants.CompletedContractId);
        var handler = registration.Resolve(scope.ServiceProvider);
        var payload = new HumanTaskCompletedEvent
        {
            EventId = "completion-1",
            HumanTaskKey = new RuntimeInstanceKey("tenant", "task"),
            HumanTaskPin = new RuntimeDescriptorPin
            {
                Ref = new DescriptorRef("humantask", "review", 1),
                ContractHash = Hash("contract", "Contract"),
                DefinitionHash = Hash("definition", "Definition")
            },
            Outcome = "Approved"
        };
        var message = new OutboxMessage
        {
            Metadata = new OutboxMessageMetadata
            {
                MessageId = payload.EventId,
                ContractId = HumanTaskDeliveryConstants.CompletedContractId,
                RequiredConsumerIds = [],
                OccurredAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            },
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            Integrity = Hash("integrity", "Integrity")
        };
        var stopwatch = Stopwatch.StartNew();

        var result = await handler.HandleAsync(new OutboxDeliveryContext
        {
            Message = message,
            Lease = new OutboxDeliveryLease { OwnerId = "owner", Fence = 1, Attempt = 1, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) },
            AttemptDeadline = DateTimeOffset.UtcNow.AddMilliseconds(50),
            Services = scope.ServiceProvider
        });

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        result.Should().Be(OutboxDeliveryOutcome.Accepted);
    }

    private static CanonicalHash Hash(string value, string purpose) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Runtime",
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "runtime-v1"
    };

    private sealed class NonCooperativeLocalEventBus : ILocalEventBus
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
            => _never.Task;

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : ILocalEvent
            => _never.Task;
    }
}
