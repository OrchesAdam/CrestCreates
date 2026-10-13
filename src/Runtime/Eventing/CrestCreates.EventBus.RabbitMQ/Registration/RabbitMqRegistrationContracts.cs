using System.Text.Json.Serialization.Metadata;
using CrestCreates.Event.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Registration;

/// <summary>
/// Closed, compilation-safe host invocation for one subscription binding. The generated code resolves
/// the handler from the delivery scope and calls the handler method with the exact payload type; no
/// reflection is involved.
/// </summary>
public delegate Task RabbitMqHandlerInvoker(
    IServiceProvider serviceProvider,
    object payload,
    CancellationToken cancellationToken);

/// <summary>
/// Manual publish declaration for events that are not contributed through the generated
/// [CrestEvent] pipeline (third-party events with an explicit typed codec) or explicit routing
/// overrides for first-party events. Keyed by exact payload type; the descriptor itself always
/// comes from IEventRegistry.
/// </summary>
public sealed record RabbitMqEventDeclaration
{
    public required Type PayloadType { get; init; }
    public string? RoutingKey { get; init; }
    public Func<JsonTypeInfo?>? CodecFactory { get; init; }
    public required string Origin { get; init; }
}

/// <summary>
/// One compile-time subscription declaration. Defaults (exchange/queue/routing/prefetch) stay null
/// until freeze resolves them against the frozen registry, so registration order never matters.
/// </summary>
public sealed record RabbitMqSubscriptionBinding
{
    public required string EventName { get; init; }
    public int? Version { get; init; }
    public required Type PayloadType { get; init; }
    public required Type HandlerType { get; init; }
    public required string HandlerMethod { get; init; }
    public string? Exchange { get; init; }
    public string? Queue { get; init; }
    public string? RoutingKey { get; init; }
    public int? PrefetchCount { get; init; }
    public required string Origin { get; init; }
    public required RabbitMqHandlerInvoker Invoker { get; init; }
}

/// <summary>
/// Per-assembly static contribution of subscription bindings and manual event declarations. Generated
/// contributors are activated only through their explicit AddRabbitMqSubscriptions() extension; there
/// is no assembly scanning and no fallback.
/// </summary>
public interface IRabbitMqRegistrationContributor
{
    /// <summary>Source description (assembly simple name) used for stable ordering and diagnostics.</summary>
    string Origin { get; }

    IReadOnlyList<RabbitMqEventDeclaration> Events { get; }

    IReadOnlyList<RabbitMqSubscriptionBinding> Subscriptions { get; }
}

/// <summary>
/// Explicit, static mapping from a legacy wire event identity (old short/CLR name written by the
/// pre-static-binding publisher) to a canonical descriptor identity. Aliases never redefine events;
/// version-less legacy messages are only interpretable through the version pinned here.
/// </summary>
public sealed record RabbitMqLegacyWireAlias(string Alias, string CanonicalName, int Version);

/// <summary>Frozen publish binding: canonical descriptor + exact codec + effective routing key.</summary>
public sealed record RabbitMqPublishBinding(
    GeneratedEventDescriptor Descriptor,
    JsonTypeInfo Codec,
    string RoutingKey,
    string Origin);

/// <summary>Stable wire execution identity: the frozen key of the additive envelope fields.</summary>
public readonly record struct RabbitMqWireIdentity(string EventId, int EventVersion);

/// <summary>One ordered handler entry inside a queue plan.</summary>
public sealed record RabbitMqHandlerBinding(
    Type HandlerType,
    string HandlerMethod,
    string Origin,
    RabbitMqHandlerInvoker Invoker);

/// <summary>One physical queue's consume plan (one consumer per queue; handlers run sequentially).</summary>
public sealed record RabbitMqQueuePlan(
    string Queue,
    int PrefetchCount,
    IReadOnlyList<RabbitMqQueueDeclaration> Declarations,
    IReadOnlyDictionary<RabbitMqWireIdentity, IReadOnlyList<RabbitMqHandlerBinding>> HandlersByIdentity);

/// <summary>One (exchange, routing key) binding that delivers an identity to a queue plan.</summary>
public sealed record RabbitMqQueueDeclaration(string Exchange, string RoutingKey, RabbitMqWireIdentity Identity);

/// <summary>Result of resolving a received envelope's wire identity against the frozen registry.</summary>
public sealed record RabbitMqWireResolution(RabbitMqPublishBinding? Binding, string? FailureReason)
{
    public bool Success => Binding is not null;
}

/// <summary>
/// Host-scoped frozen read model: publish index by concrete payload type, wire index by
/// (EventId, EventVersion), legacy alias table and per-queue consume plans. Immutable after freeze;
/// holds only static metadata, codecs and closed invokers.
/// </summary>
public interface IRabbitMqBindingRegistry
{
    bool PublisherOnly { get; }

    IReadOnlyList<RabbitMqQueuePlan> QueuePlans { get; }

    IReadOnlyCollection<Type> PublishableEventTypes { get; }

    bool TryGetPublishBinding(Type eventType, out RabbitMqPublishBinding? binding);

    /// <summary>
    /// Resolves the envelope's wire identity. New-format envelopes carry EventId + EventVersion
    /// (execution key) and the canonical Name for cross-checking; legacy envelopes (both fields absent)
    /// resolve only through the explicit static alias table. Anything in between is rejected.
    /// </summary>
    RabbitMqWireResolution ResolveWire(string eventType, string? eventId, int? eventVersion);
}
