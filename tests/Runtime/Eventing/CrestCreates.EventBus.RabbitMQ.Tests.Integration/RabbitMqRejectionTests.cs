using CrestCreates.Event.Abstractions;
using CrestCreates.RabbitMq.TestHandlersA;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

/// <summary>
/// Rejection matrix: malformed envelopes, unknown/incomplete/mismatched identities, unknown payloads and
/// queue membership violations are rejected before any business call and land on the configured dead
/// letter path. No fallback resolution or string-based type discovery exists anywhere.
/// </summary>
public class RabbitMqRejectionTests : RabbitMqIntegrationTestBase
{
    public RabbitMqRejectionTests(ITestOutputHelper output) : base(output)
    {
    }

    private static string OrderPlacedId => GeneratedEventDescriptor.GenerateId("orders.order-placed");

    private static string Envelope(string eventType, string? eventId, int? eventVersion, string payload = "{}")
    {
        var idJson = eventId is null ? "null" : $"\"{eventId}\"";
        var versionJson = eventVersion?.ToString() ?? "null";
        var escapedPayload = payload.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $$"""{"eventType":"{{eventType}}","eventId":{{idJson}},"eventVersion":{{versionJson}},"payload":"{{escapedPayload}}","headers":{},"timestamp":"2026-10-13T00:00:00Z"}""";
    }

    private async Task AssertDeadLetteredWithoutBusinessCallsAsync()
    {
        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages") == 1,
            TimeSpan.FromSeconds(10))).Should().BeTrue("the message must land on the dead-letter path");
        (await GetQueueDepthAsync(VHost, QueueNames.Orders, "messages")).Should().Be(0);
        RabbitMqTestHooks.CallLog.Should().BeEmpty("no handler may run for rejected messages");
    }

    [Fact]
    public async Task Malformed_json_body_is_dead_lettered()
    {
        var host = await StartHostAsync();
        await PublishRawAsync(VHost, QueueNames.Orders, "this-is-not-json");
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Unknown_wire_identity_is_dead_lettered()
    {
        var host = await StartHostAsync();
        await PublishRawAsync(VHost, QueueNames.Orders, Envelope("orders.order-placed", "evt_does_not_exist", 1));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Unknown_version_is_dead_lettered_without_downgrade()
    {
        var host = await StartHostAsync();
        await PublishRawAsync(VHost, QueueNames.Orders, Envelope("orders.order-placed", OrderPlacedId, 2));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Incomplete_identity_is_dead_lettered()
    {
        var host = await StartHostAsync();
        await PublishRawAsync(VHost, QueueNames.Orders, Envelope("orders.order-placed", OrderPlacedId, null));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Name_mismatch_is_dead_lettered()
    {
        var host = await StartHostAsync();
        await PublishRawAsync(VHost, QueueNames.Orders, Envelope("inventory.stock-reserved", OrderPlacedId, 1));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Null_payload_is_dead_lettered()
    {
        var host = await StartHostAsync();
        await PublishRawAsync(VHost, QueueNames.Orders, Envelope("orders.order-placed", OrderPlacedId, 1, payload: "null"));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Unparseable_payload_is_dead_lettered()
    {
        var host = await StartHostAsync();
        // A JSON string cannot bind to the decimal Amount property: deserialization must fail before
        // any handler runs (the parser is strict about types, never reflection-fallback).
        await PublishRawAsync(
            VHost,
            QueueNames.Orders,
            Envelope("orders.order-placed", OrderPlacedId, 1, payload: """{"amount":"not-a-number"}"""));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Event_not_declared_by_the_queue_is_dead_lettered()
    {
        var host = await StartHostAsync();

        // The billing identity is valid for the registry but is not part of the a.orders queue plan.
        var billingId = GeneratedEventDescriptor.GenerateId("billing.invoice-issued");
        await PublishRawAsync(VHost, QueueNames.Orders, Envelope("billing.invoice-issued", billingId, 1));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Unknown_legacy_identity_without_alias_is_dead_lettered()
    {
        var host = await StartHostAsync();

        // Legacy shape (no EventId/EventVersion) without an explicit static alias must be rejected;
        // there is no short-name scan fallback.
        await PublishRawAsync(VHost, QueueNames.Orders, Envelope("OrderPlacedEvent", null, null));
        await AssertDeadLetteredWithoutBusinessCallsAsync();
    }

    [Fact]
    public async Task Legacy_alias_resolves_only_when_statically_declared()
    {
        var host = await StartHostAsync(builder => builder
            .AddLegacyWireAlias("OrderPlacedEvent", "orders.order-placed", 1)
            .AddLegacyWireAlias("MyApp.Orders.OrderPlacedEvent", "orders.order-placed", 1));

        await PublishRawAsync(
            VHost,
            QueueNames.Orders,
            Envelope("OrderPlacedEvent", null, null, payload: """{"orderId":"legacy-1","amount":1,"tags":[],"occurredOn":"2026-10-13T00:00:00Z"}"""));

        var called = await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:legacy-1:"))),
            TimeSpan.FromSeconds(10));
        var queueDump = string.Join("; ", await GetQueueNamesAsync(VHost));
        called.Should().BeTrue(
            $"the explicit alias must resolve the legacy identity " +
            $"[queues: {queueDump}] [calls: {string.Join(",", RabbitMqTestHooks.CallLog)}]");
        (await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages")).Should().Be(0);
    }
}
