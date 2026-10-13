using System.Text.Json.Serialization;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

// ── Test payloads and source-generated codecs ──

public sealed class OrderPlacedPayload
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class InvoiceIssuedPayload
{
    public string InvoiceId { get; set; } = string.Empty;
}

public sealed class LocalScopedPayload
{
    public string Value { get; set; } = string.Empty;
}

public sealed class ThirdPartyPayload
{
    public string ExternalId { get; set; } = string.Empty;
}

/// <summary>Descriptor exists but no codec is ever registered for this payload.</summary>
public sealed class NoCodecPayload
{
    public string Value { get; set; } = string.Empty;
}

[JsonSerializable(typeof(OrderPlacedPayload))]
[JsonSerializable(typeof(InvoiceIssuedPayload))]
[JsonSerializable(typeof(LocalScopedPayload))]
[JsonSerializable(typeof(ThirdPartyPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class FreezeTestJsonContext : JsonSerializerContext;

public static class FreezeTestCodecs
{
    private static int _registered;

    /// <summary>
    /// Registers the first-party test payload codecs into the production static codec registry
    /// (idempotent TryAdd semantics), mirroring the generated AddEventDescriptors() contribution path.
    /// ThirdPartyPayload is intentionally excluded: it exercises the explicit typed-codec path.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        EventJsonCodecRegistry.Register(typeof(OrderPlacedPayload), static () => FreezeTestJsonContext.Default.OrderPlacedPayload);
        EventJsonCodecRegistry.Register(typeof(InvoiceIssuedPayload), static () => FreezeTestJsonContext.Default.InvoiceIssuedPayload);
        EventJsonCodecRegistry.Register(typeof(LocalScopedPayload), static () => FreezeTestJsonContext.Default.LocalScopedPayload);
    }
}

// ── Descriptor providers ──

public sealed class OrderPlacedDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() =>
    [
        new GeneratedEventDescriptor
        {
            Id = GeneratedEventDescriptor.GenerateId("orders.order-placed"),
            Name = "orders.order-placed",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(OrderPlacedPayload),
            Scope = EventScope.Integration,
        },
    ];
}

public sealed class InvoiceIssuedDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() =>
    [
        new GeneratedEventDescriptor
        {
            Id = GeneratedEventDescriptor.GenerateId("billing.invoice-issued"),
            Name = "billing.invoice-issued",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(InvoiceIssuedPayload),
            Scope = EventScope.Integration,
        },
    ];
}

public sealed class LocalScopedDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() =>
    [
        new GeneratedEventDescriptor
        {
            Id = GeneratedEventDescriptor.GenerateId("local.only"),
            Name = "local.only",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(LocalScopedPayload),
            Scope = EventScope.Local,
        },
    ];
}

public sealed class ThirdPartyDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() =>
    [
        new GeneratedEventDescriptor
        {
            Id = GeneratedEventDescriptor.GenerateId("third.party-created"),
            Name = "third.party-created",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(ThirdPartyPayload),
            Scope = EventScope.Integration,
        },
    ];
}

public sealed class NoCodecDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() =>
    [
        new GeneratedEventDescriptor
        {
            Id = GeneratedEventDescriptor.GenerateId("events.no-codec"),
            Name = "events.no-codec",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(NoCodecPayload),
            Scope = EventScope.Integration,
        },
    ];
}

// ── Contributors ──

public sealed class TestHandler
{
    public Task HandleAsync(object payload, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class TestContributor : IRabbitMqRegistrationContributor
{
    private readonly List<RabbitMqEventDeclaration> _events = new();
    private readonly List<RabbitMqSubscriptionBinding> _subscriptions = new();

    public TestContributor(string origin) => Origin = origin;

    public string Origin { get; }

    public IReadOnlyList<RabbitMqEventDeclaration> Events => _events;

    public IReadOnlyList<RabbitMqSubscriptionBinding> Subscriptions => _subscriptions;

    public TestContributor AddSubscription(
        string eventName,
        Type payloadType,
        string? queue = null,
        int? version = null,
        int? prefetchCount = null,
        string handlerMethod = "HandleAsync",
        Type? handlerType = null)
    {
        _subscriptions.Add(new RabbitMqSubscriptionBinding
        {
            EventName = eventName,
            Version = version,
            PayloadType = payloadType,
            HandlerType = handlerType ?? typeof(TestHandler),
            HandlerMethod = handlerMethod,
            Queue = queue,
            PrefetchCount = prefetchCount,
            Origin = Origin,
            Invoker = static (provider, payload, cancellationToken) => Task.CompletedTask,
        });
        return this;
    }
}
