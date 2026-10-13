using CrestCreates.Event.Abstractions;
using CrestCreates.RabbitMq.TestContractsA;
using CrestCreates.RabbitMq.TestHandlersA;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

/// <summary>
/// Business retry and DLQ semantics: RetryCount = 0 terminates immediately, the delay is actually
/// observed, properties (MessageId/CorrelationId) survive redelivery, the budget is counted in the
/// persistent header, exhaustion dead-letters with the count, and a broker-side replay of the dead
/// letter is consumed by the official consumer after the budget is reset.
/// </summary>
public class RabbitMqRetryDlqTests : RabbitMqIntegrationTestBase
{
    public RabbitMqRetryDlqTests(ITestOutputHelper output) : base(output)
    {
    }

    private volatile bool _replayFailing;

    private static string OrderPlacedId => GeneratedEventDescriptor.GenerateId("orders.order-placed");

    private static string ValidEnvelope(string orderId)
    {
        var payload = $$"""{"orderId":"{{orderId}}","amount":1.5,"tags":["t"],"occurredOn":"2026-10-13T00:00:00Z"}""";
        var escaped = payload.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $$"""{"eventType":"orders.order-placed","eventId":"{{OrderPlacedId}}","eventVersion":1,"payload":"{{escaped}}","headers":{},"timestamp":"2026-10-13T00:00:00Z"}""";
    }

    [Fact]
    public async Task RetryCount_zero_terminates_on_first_failure()
    {
        var host = await StartHostAsync(options: o =>
        {
            o.RetryCount = 0;
            o.RetryDelaySeconds = 0;
        });

        var attempts = 0;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("business failure");
        };

        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(
            new OrderPlacedEvent { OrderId = "zero-1", Amount = 1m });

        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages") == 1,
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        attempts.Should().Be(1);
        RabbitMqTestHooks.CallLog.Should().ContainSingle(e => e.StartsWith("order:zero-1:"));

        var dead = await GetMessageAsync(VHost, QueueNames.Orders + ".dlq");
        dead.Should().NotBeNull();
        dead!.Headers.Should().NotContainKey("x-retry-count", "no redelivery consumed any budget");

        // The audit handler ran before the failing handler (sequential execution, stop on failure).
        RabbitMqTestHooks.CallLog.Should().Contain(e => e.StartsWith("audit:zero-1:"));
    }

    [Fact]
    public async Task Failing_delivery_is_redelivered_after_the_configured_delay_and_then_succeeds()
    {
        var host = await StartHostAsync(options: o =>
        {
            o.RetryCount = 1;
            o.RetryDelaySeconds = 2;
        });

        var attempts = 0;
        var firstAttemptAt = DateTime.MinValue;
        var secondAttemptAt = DateTime.MinValue;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                firstAttemptAt = DateTime.UtcNow;
                throw new InvalidOperationException("first attempt fails");
            }

            secondAttemptAt = DateTime.UtcNow;
            return Task.CompletedTask;
        };

        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(
            new OrderPlacedEvent { OrderId = "retry-1", Amount = 1m });

        (await WaitUntilAsync(
            () => Task.FromResult(Volatile.Read(ref attempts) == 2),
            TimeSpan.FromSeconds(15))).Should().BeTrue("the redelivered attempt must succeed");

        (secondAttemptAt - firstAttemptAt).Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromSeconds(1.5), "RetryDelaySeconds must actually be observed");

        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders, "messages") == 0,
            TimeSpan.FromSeconds(5))).Should().BeTrue("the successful retry must be acknowledged");
        (await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages")).Should().Be(0);

        // The redelivery re-runs the full queue plan (no per-handler checkpoint).
        RabbitMqTestHooks.CallLog.Count(e => e.StartsWith("audit:retry-1:")).Should().Be(2);
    }

    [Fact]
    public async Task Budget_exhaustion_dead_letters_with_preserved_identity_and_retry_count()
    {
        var host = await StartHostAsync(options: o =>
        {
            o.RetryCount = 2;
            o.RetryDelaySeconds = 0;
        });

        var attempts = 0;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("always fails");
        };

        const string messageId = "fixed-message-id-127";
        const string correlationId = "fixed-correlation-127";
        var envelope = ValidEnvelope("exhaust-1");

        // Publish with a fixed MessageId/CorrelationId through the raw harness path so property
        // preservation across redeliveries can be asserted exactly.
        await PublishRawAsync(VHost, QueueNames.Orders, envelope, messageId: messageId, correlationId: correlationId);

        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages") == 1,
            TimeSpan.FromSeconds(20))).Should().BeTrue();

        attempts.Should().Be(3, "initial attempt plus RetryCount redeliveries");

        var dead = await GetMessageAsync(VHost, QueueNames.Orders + ".dlq");
        dead.Should().NotBeNull();
        dead!.MessageId.Should().Be(messageId, "redelivery must not regenerate MessageId");
        dead.CorrelationId.Should().Be(correlationId, "correlation must survive redelivery");
        dead.Headers.Should().ContainKey("x-retry-count").WhoseValue.Should().Be("2");
        dead.Payload.Should().Contain("exhaust-1", "the body must be preserved verbatim");
    }

    [Fact]
    public async Task Dead_letter_replayed_through_the_broker_is_consumed_by_the_official_consumer()
    {
        var host = await StartHostAsync(options: o =>
        {
            o.RetryCount = 0;
            o.RetryDelaySeconds = 0;
        });

        var attempts = 0;
        _replayFailing = true;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            return _replayFailing
                ? throw new InvalidOperationException("fails until the harness replays")
                : Task.CompletedTask;
        };

        await PublishRawAsync(VHost, QueueNames.Orders, ValidEnvelope("replay-1"));

        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages") == 1,
            TimeSpan.FromSeconds(10))).Should().BeTrue();
        Volatile.Read(ref attempts).Should().Be(1, "the initial delivery must fail exactly once");

        // Harness-side replay: read the dead letter (ack_requeue_false removes it), reset the business
        // retry budget, republish to the original queue through the default exchange, then the official
        // consumer must process it. No public replay API is involved.
        var dead = await GetMessageAsync(VHost, QueueNames.Orders + ".dlq");
        dead.Should().NotBeNull();
        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages") == 0,
            TimeSpan.FromSeconds(5))).Should().BeTrue("the management read must acknowledge the dead letter");

        _replayFailing = false;
        await PublishRawAsync(VHost, QueueNames.Orders, dead!.Payload, headers: new Dictionary<string, object?>
        {
            ["x-retry-count"] = "0",
        });

        (await WaitUntilAsync(
            () => Task.FromResult(Volatile.Read(ref attempts) == 2),
            TimeSpan.FromSeconds(10))).Should().BeTrue("the replayed message must be redelivered to the official consumer");
        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders, "messages") == 0,
            TimeSpan.FromSeconds(5))).Should().BeTrue();

        await Task.Delay(500);
        (await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages")).Should().Be(0,
            "the successfully replayed message must not be dead-lettered again");
        RabbitMqTestHooks.CallLog.Count(e => e.StartsWith("order:replay-1:")).Should().Be(2);
    }
}
