using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.RabbitMQ.Registration;

/// <summary>
/// Accumulated composition state for the single AddRabbitMqEventBus entry. Multiple calls merge into
/// the same instance so module forwarding and explicit host configuration cannot overwrite each other.
/// </summary>
public sealed class RabbitMqCompositionConfiguration
{
    internal List<RabbitMqEventDeclaration> EventDeclarations { get; } = new();

    internal List<RabbitMqLegacyWireAlias> LegacyAliases { get; } = new();

    internal List<IRabbitMqRegistrationContributor> ManualContributors { get; } = new();

    internal HashSet<Type> RequiredEvents { get; } = new();

    internal HashSet<Type> RequiredHandlers { get; } = new();

    internal List<JsonSerializerContext> ContextCodecs { get; } = new();

    internal bool PublisherOnly { get; set; }
}

/// <summary>
/// Composition builder for <c>AddRabbitMqEventBus</c>. All configuration is static and explicit; there
/// is no string-based discovery. Every entry participates in the same freeze validation.
/// </summary>
public sealed class RabbitMqEventBusBuilder
{
    private readonly RabbitMqCompositionConfiguration _configuration;

    internal RabbitMqEventBusBuilder(RabbitMqCompositionConfiguration configuration)
        => _configuration = configuration;

    /// <summary>
    /// Publisher-only host: zero subscriptions are allowed and no consumer starts. Published events
    /// still require a complete frozen descriptor + codec.
    /// </summary>
    public RabbitMqEventBusBuilder PublisherOnly()
    {
        _configuration.PublisherOnly = true;
        return this;
    }

    /// <summary>
    /// Declares a publishable event explicitly: supplies the typed codec for third-party events whose
    /// owning assembly does not use the [CrestEvent] pipeline, and/or overrides the effective routing
    /// key of a first-party event. The descriptor itself must still come from IEventRegistry.
    /// </summary>
    public RabbitMqEventBusBuilder AddPublishedEvent<TEvent>(JsonTypeInfo<TEvent>? codec = null, string? routingKey = null)
        where TEvent : class
    {
        _configuration.EventDeclarations.Add(new RabbitMqEventDeclaration
        {
            PayloadType = typeof(TEvent),
            RoutingKey = routingKey,
            CodecFactory = codec is null ? null : () => codec,
            Origin = ResolveOriginName(typeof(TEvent)),
        });
        return this;
    }

    /// <summary>
    /// Explicit static alias for legacy wire identities written by the pre-canonical publisher.
    /// Aliases never redefine events and must pin the exact canonical version.
    /// </summary>
    public RabbitMqEventBusBuilder AddLegacyWireAlias(string alias, string canonicalName, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        _configuration.LegacyAliases.Add(new RabbitMqLegacyWireAlias(alias, canonicalName, version));
        return this;
    }

    /// <summary>Host-side required manifest: this event must be publishable after freeze.</summary>
    public RabbitMqEventBusBuilder RequirePublishableEvent<TEvent>() where TEvent : class
    {
        _configuration.RequiredEvents.Add(typeof(TEvent));
        return this;
    }

    /// <summary>Host-side required manifest: this handler type must appear in the frozen subscriptions.</summary>
    public RabbitMqEventBusBuilder RequireSubscription<THandler>()
    {
        _configuration.RequiredHandlers.Add(typeof(THandler));
        return this;
    }

    /// <summary>
    /// Adds a fully explicit registration contributor (low-level static assembly or third-party
    /// bindings). It passes through the same freeze validation as generated contributors.
    /// </summary>
    public RabbitMqEventBusBuilder AddContributor(IRabbitMqRegistrationContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        _configuration.ManualContributors.Add(contributor);
        return this;
    }

    internal static string ResolveOriginName(Type type)
        => type.Assembly.GetName().Name ?? type.Assembly.FullName ?? "<unknown>";
}
