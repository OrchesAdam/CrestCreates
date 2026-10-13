using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;

namespace CrestCreates.EventBus.RabbitMQ.Registration;

/// <summary>Deterministic composition failure: every violated declaration, sorted and attributed.</summary>
public sealed class RabbitMqCompositionException : Exception
{
    public RabbitMqCompositionException(IReadOnlyList<string> violations)
        : base("RabbitMQ composition freeze failed:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(v => " - " + v)))
        => Violations = violations;

    public IReadOnlyList<string> Violations { get; }
}

public sealed record RabbitMqFreezeInput(
    EventRegistry EventRegistry,
    IReadOnlyList<IRabbitMqRegistrationContributor> Contributors,
    RabbitMqCompositionConfiguration Configuration,
    RabbitMqOptions Options);

/// <summary>
/// Merge validation and freeze for the RabbitMQ transport binding registry. All declaration checks run
/// here, before any broker I/O: identity uniqueness, canonical reference resolution, exact payload type
/// matching, codec availability, alias integrity, queue plan conflicts and required manifests.
/// </summary>
public static class RabbitMqBindingFreeze
{
    public static RabbitMqBindingRegistry Freeze(RabbitMqFreezeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var registry = input.EventRegistry;
        var options = input.Options;
        var configuration = input.Configuration;
        var violations = new List<string>();

        if (registry.State != RegistryState.Built)
        {
            throw new RabbitMqCompositionException(
            [
                "EventRegistry is not built. RabbitMQ composition must run after the event kernel bootstrap " +
                "(the AddRabbitMqEventBus composition service builds it); do not construct the registry manually.",
            ]);
        }

        var descriptors = registry.GetAll();
        var active = descriptors.Where(d => d.State == DescriptorState.Active).ToList();

        // ── Identity uniqueness (defense in depth on top of registry validation) ──
        foreach (var group in active.GroupBy(d => d.Id, StringComparer.Ordinal))
        {
            var distinct = group.Select(d => (d.Name, d.Version, d.PayloadType)).Distinct().ToList();
            if (distinct.Count > 1)
            {
                violations.Add(
                    $"Event Id '{group.Key}' maps to multiple canonical identities: " +
                    string.Join(", ", distinct.Select(d => $"{d.Name} v{d.Version} ({d.PayloadType.FullName})")) + ".");
            }
        }

        foreach (var group in active.GroupBy(d => (d.Name, d.Version)))
        {
            if (group.Count() > 1 && group.Select(d => d.PayloadType).Distinct().Count() > 1)
            {
                violations.Add(
                    $"Canonical event '{group.Key.Name}' v{group.Key.Version} is declared with different payload types: " +
                    string.Join(", ", group.Select(d => d.PayloadType.FullName)) + ".");
            }
        }

        // ── Manual publish declarations (keyed by exact payload type) ──
        var manualByType = new Dictionary<Type, RabbitMqEventDeclaration>();
        foreach (var declaration in configuration.EventDeclarations)
        {
            if (!manualByType.TryAdd(declaration.PayloadType, declaration))
            {
                violations.Add(
                    $"Duplicate manual publish declaration for payload type '{declaration.PayloadType.FullName}' " +
                    $"(origins: '{manualByType[declaration.PayloadType].Origin}' and '{declaration.Origin}').");
            }
        }

        foreach (var declaration in manualByType.Values)
        {
            var descriptor = registry.GetByPayloadType(declaration.PayloadType);
            if (descriptor is null)
            {
                violations.Add(
                    $"Manual publish declaration for '{declaration.PayloadType.FullName}' (origin '{declaration.Origin}') " +
                    "has no Active event descriptor. Declare [CrestEvent] on the type (and activate the owning " +
                    "assembly's AddEventDescriptors()) or provide an explicit IEventDescriptorProvider.");
            }
            else if (descriptor.Scope != EventScope.Integration)
            {
                violations.Add(
                    $"Manual publish declaration for '{declaration.PayloadType.FullName}' resolves to event " +
                    $"'{descriptor.Name}' with scope {descriptor.Scope}; RabbitMQ publishes Integration events only.");
            }
        }

        var manualContributors = configuration.ManualContributors.ToList();
        var contributors = input.Contributors.Concat(manualContributors)
            .Distinct()
            .OrderBy(c => c.Origin, StringComparer.Ordinal)
            .ToList();

        // ── Publish index ──
        var publishBindings = new List<RabbitMqPublishBinding>();
        var publishByDescriptor = new Dictionary<(string Id, int Version), RabbitMqPublishBinding>();
        foreach (var descriptor in active.Where(d => d.Scope == EventScope.Integration))
        {
            manualByType.TryGetValue(descriptor.PayloadType, out var manual);
            var codec = ResolveCodec(descriptor.PayloadType, manual?.CodecFactory, configuration, out var codecProblem);
            if (codec is null)
            {
                // Not a freeze error by itself: a host is not required to publish every registered
                // event. It becomes an error when the event is required or subscribed (below), and a
                // typed publish-time diagnostic otherwise.
                _ = codecProblem;
                continue;
            }

            var binding = new RabbitMqPublishBinding(
                descriptor,
                codec,
                manual?.RoutingKey ?? descriptor.Name,
                manual?.Origin ?? "$generated");
            publishBindings.Add(binding);
            publishByDescriptor[(descriptor.Id, descriptor.Version)] = binding;
        }

        // ── Required publishable manifest ──
        foreach (var requiredType in configuration.RequiredEvents.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var descriptor = registry.GetByPayloadType(requiredType);
            if (descriptor is null)
            {
                violations.Add(
                    $"Required publishable event '{requiredType.FullName}' has no Active descriptor after the event " +
                    "kernel bootstrap. Activate the owning assembly's AddEventDescriptors() extension.");
                continue;
            }

            if (descriptor.Scope != EventScope.Integration)
            {
                violations.Add(
                    $"Required publishable event '{requiredType.FullName}' resolves to scope {descriptor.Scope}; " +
                    "RabbitMQ publishes Integration events only.");
                continue;
            }

            if (!publishByDescriptor.ContainsKey((descriptor.Id, descriptor.Version)))
            {
                ResolveCodec(descriptor.PayloadType, null, configuration, out var problem);
                violations.Add(
                    $"Required publishable event '{descriptor.Name}' v{descriptor.Version} has no frozen codec: {problem}. " +
                    "Activate the owning assembly's AddEventDescriptors() extension or supply an explicit typed codec.");
            }
        }

        // ── Subscriptions ──
        var resolved = new List<ResolvedSubscription>();
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contributor in contributors)
        {
            foreach (var binding in contributor.Subscriptions)
            {
                var descriptor = ResolveDescriptor(registry, binding, violations);
                if (descriptor is null)
                {
                    continue;
                }

                if (descriptor.Scope != EventScope.Integration)
                {
                    violations.Add(
                        $"Subscription '{binding.EventName}' (handler {binding.HandlerType.FullName}.{binding.HandlerMethod}, " +
                        $"origin '{contributor.Origin}') resolves to scope {descriptor.Scope}; RabbitMQ consumes " +
                        "Integration events only.");
                    continue;
                }

                if (descriptor.PayloadType != binding.PayloadType)
                {
                    violations.Add(
                        $"Subscription '{binding.EventName}' v{descriptor.Version} (handler " +
                        $"{binding.HandlerType.FullName}.{binding.HandlerMethod}, origin '{contributor.Origin}') declares " +
                        $"payload '{binding.PayloadType.FullName}' but the canonical descriptor payload is " +
                        $"'{descriptor.PayloadType.FullName}'.");
                    continue;
                }

                manualByType.TryGetValue(descriptor.PayloadType, out var manual);
                var codec = ResolveCodec(descriptor.PayloadType, manual?.CodecFactory, configuration, out var problem);
                if (codec is null)
                {
                    violations.Add(
                        $"Subscription '{binding.EventName}' v{descriptor.Version} (handler " +
                        $"{binding.HandlerType.FullName}.{binding.HandlerMethod}, origin '{contributor.Origin}') has no " +
                        $"frozen codec for '{descriptor.PayloadType.FullName}': {problem}. Activate the owning " +
                        "assembly's AddEventDescriptors() extension or supply an explicit typed codec.");
                    continue;
                }

                if (binding.PrefetchCount is { } prefetch && (prefetch < 1 || prefetch > ushort.MaxValue))
                {
                    violations.Add(
                        $"Subscription '{binding.EventName}' (origin '{contributor.Origin}') declares PrefetchCount " +
                        $"{prefetch}; the valid range is 1..{ushort.MaxValue}.");
                    continue;
                }

                var queue = binding.Queue ?? $"crestcreates.{binding.EventName}";
                if (string.IsNullOrWhiteSpace(queue))
                {
                    violations.Add($"Subscription '{binding.EventName}' (origin '{contributor.Origin}') resolves to an empty queue name.");
                    continue;
                }

                var exchange = binding.Exchange ?? options.DefaultExchange;
                if (string.IsNullOrWhiteSpace(exchange))
                {
                    violations.Add($"Subscription '{binding.EventName}' (origin '{contributor.Origin}') resolves to an empty exchange name.");
                    continue;
                }

                var routingKey = binding.RoutingKey ?? descriptor.Name;
                if (string.IsNullOrWhiteSpace(routingKey))
                {
                    violations.Add($"Subscription '{binding.EventName}' (origin '{contributor.Origin}') resolves to an empty routing key.");
                    continue;
                }

                var duplicateKey = string.Join("|",
                    contributor.Origin, binding.HandlerType.FullName, binding.HandlerMethod,
                    descriptor.Id, descriptor.Version.ToString(CultureInfo.InvariantCulture),
                    exchange, queue, routingKey);
                if (!duplicateKeys.Add(duplicateKey))
                {
                    violations.Add(
                        $"Duplicate subscription binding: handler {binding.HandlerType.FullName}.{binding.HandlerMethod} " +
                        $"(origin '{contributor.Origin}') is declared more than once for event '{descriptor.Name}' " +
                        $"v{descriptor.Version} on queue '{queue}'.");
                    continue;
                }

                resolved.Add(new ResolvedSubscription(
                    descriptor,
                    contributor.Origin,
                    new RabbitMqHandlerBinding(binding.HandlerType, binding.HandlerMethod, contributor.Origin, binding.Invoker),
                    exchange,
                    queue,
                    routingKey,
                    binding.PrefetchCount ?? options.DefaultPrefetchCount));
            }
        }

        // ── Required subscriptions manifest ──
        foreach (var requiredHandler in configuration.RequiredHandlers.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (!resolved.Any(r => r.Handler.HandlerType == requiredHandler))
            {
                violations.Add(
                    $"Required subscription handler '{requiredHandler.FullName}' is not part of the frozen " +
                    "subscriptions. Activate the owning assembly's AddRabbitMqSubscriptions() extension.");
            }
        }

        // ── Queue plans (one physical queue = one consume plan) ──
        var plans = new List<RabbitMqQueuePlan>();
        foreach (var queueGroup in resolved.GroupBy(r => r.Queue, StringComparer.Ordinal))
        {
            var prefetchValues = queueGroup.Select(r => r.PrefetchCount).Distinct().ToList();
            if (prefetchValues.Count > 1)
            {
                violations.Add(
                    $"Queue '{queueGroup.Key}' is declared with conflicting prefetch counts: " +
                    string.Join(", ", prefetchValues.OrderBy(v => v)) + ". Align PrefetchCount or split the queues.");
                continue;
            }

            var declarations = queueGroup
                .Select(r => new RabbitMqQueueDeclaration(r.Exchange, r.RoutingKey, new RabbitMqWireIdentity(r.Descriptor.Id, r.Descriptor.Version)))
                .Distinct()
                .OrderBy(d => d.Exchange, StringComparer.Ordinal)
                .ThenBy(d => d.RoutingKey, StringComparer.Ordinal)
                .ThenBy(d => d.Identity.EventId, StringComparer.Ordinal)
                .ToList();

            var handlersByIdentity = queueGroup
                .GroupBy(r => new RabbitMqWireIdentity(r.Descriptor.Id, r.Descriptor.Version))
                .ToFrozenDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<RabbitMqHandlerBinding>)g
                        .Select(r => r.Handler)
                        .OrderBy(h => h.Origin, StringComparer.Ordinal)
                        .ThenBy(h => h.HandlerType.FullName, StringComparer.Ordinal)
                        .ThenBy(h => h.HandlerMethod, StringComparer.Ordinal)
                        .ToList());

            plans.Add(new RabbitMqQueuePlan(
                queueGroup.Key,
                prefetchValues[0],
                declarations,
                handlersByIdentity));
        }

        plans = plans.OrderBy(p => p.Queue, StringComparer.Ordinal).ToList();

        if (!configuration.PublisherOnly && resolved.Count == 0)
        {
            violations.Add(
                "No subscriptions are declared while the consumer is enabled. Declare [RabbitMqSubscribe] handlers " +
                "and activate their generated AddRabbitMqSubscriptions() extension, or switch the host to " +
                "PublisherOnly() explicitly.");
        }

        // ── Legacy aliases ──
        var aliasTable = new Dictionary<string, RabbitMqPublishBinding>(StringComparer.Ordinal);
        var canonicalNames = descriptors.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var alias in configuration.LegacyAliases)
        {
            if (canonicalNames.Contains(alias.Alias))
            {
                violations.Add(
                    $"Legacy alias '{alias.Alias}' collides with an existing canonical event name; aliases must be " +
                    "unambiguous and cannot shadow canonical identities.");
                continue;
            }

            if (aliasTable.ContainsKey(alias.Alias))
            {
                violations.Add($"Duplicate legacy alias '{alias.Alias}'.");
                continue;
            }

            RabbitMqPublishBinding? target = null;
            var exact = registry.GetByNameAndVersion(alias.CanonicalName, alias.Version);
            if (exact is { State: DescriptorState.Active, Scope: EventScope.Integration } &&
                publishByDescriptor.TryGetValue((exact.Id, exact.Version), out var found))
            {
                target = found;
            }

            if (target is null)
            {
                violations.Add(
                    $"Legacy alias '{alias.Alias}' targets '{alias.CanonicalName}' v{alias.Version}, which is not an " +
                    "Active Integration event with a frozen codec. Fix the alias or activate the owning assembly's " +
                    "AddEventDescriptors() extension.");
                continue;
            }

            aliasTable[alias.Alias] = target;
        }

        if (violations.Count > 0)
        {
            var ordered = violations.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
            throw new RabbitMqCompositionException(ordered);
        }

        return new RabbitMqBindingRegistry(
            configuration.PublisherOnly,
            publishBindings,
            plans,
            aliasTable);
    }

    private static GeneratedEventDescriptor? ResolveDescriptor(
        EventRegistry registry,
        RabbitMqSubscriptionBinding binding,
        List<string> violations)
    {
        List<GeneratedEventDescriptor> candidates;
        if (binding.Version is { } version)
        {
            var exact = registry.GetByNameAndVersion(binding.EventName, version);
            if (exact is null)
            {
                violations.Add(
                    $"Subscription '{binding.EventName}' v{version} (handler " +
                    $"{binding.HandlerType.FullName}.{binding.HandlerMethod}, origin '{binding.Origin}') has no matching " +
                    "descriptor version. Check the canonical name/version or the owning assembly's declaration.");
                return null;
            }

            candidates = [exact];
        }
        else
        {
            candidates = registry.GetAllVersions(binding.EventName)
                .Where(d => d.State == DescriptorState.Active)
                .ToList();
            if (candidates.Count == 0)
            {
                violations.Add(
                    $"Subscription '{binding.EventName}' (handler {binding.HandlerType.FullName}.{binding.HandlerMethod}, " +
                    $"origin '{binding.Origin}') has no Active descriptor. Declare [CrestEvent] on the payload type and " +
                    "activate the owning assembly's AddEventDescriptors() extension.");
                return null;
            }

            if (candidates.Count > 1)
            {
                violations.Add(
                    $"Subscription '{binding.EventName}' (handler {binding.HandlerType.FullName}.{binding.HandlerMethod}, " +
                    $"origin '{binding.Origin}') is ambiguous across Active versions " +
                    string.Join(", ", candidates.Select(c => "v" + c.Version)) + "; pin Version explicitly.");
                return null;
            }
        }

        var descriptor = candidates[0];
        if (descriptor.State != DescriptorState.Active)
        {
            violations.Add(
                $"Subscription '{binding.EventName}' v{descriptor.Version} (handler " +
                $"{binding.HandlerType.FullName}.{binding.HandlerMethod}, origin '{binding.Origin}') targets a " +
                $"{descriptor.State} descriptor; only Active versions can be consumed.");
            return null;
        }

        return descriptor;
    }

    private static JsonTypeInfo? ResolveCodec(
        Type payloadType,
        Func<JsonTypeInfo?>? manualFactory,
        RabbitMqCompositionConfiguration configuration,
        out string problem)
    {
        if (manualFactory is not null)
        {
            var manual = manualFactory();
            if (manual is null)
            {
                problem = "the explicit manual codec factory returned null";
                return null;
            }

            if (manual.Type != payloadType)
            {
                problem = $"the explicit manual codec describes '{manual.Type.FullName}' instead of '{payloadType.FullName}'";
                return null;
            }

            problem = string.Empty;
            return manual;
        }

        var staticInfo = EventJsonCodecRegistry.Resolve(payloadType);
        if (staticInfo is not null)
        {
            if (staticInfo.Type != payloadType)
            {
                problem = $"the generated codec describes '{staticInfo.Type.FullName}' instead of '{payloadType.FullName}'";
                return null;
            }

            problem = string.Empty;
            return staticInfo;
        }

        foreach (var context in configuration.ContextCodecs)
        {
            var info = context.GetTypeInfo(payloadType);
            if (info is not null)
            {
                if (info.Type != payloadType)
                {
                    problem = $"the context codec describes '{info.Type.FullName}' instead of '{payloadType.FullName}'";
                    return null;
                }

                problem = string.Empty;
                return info;
            }
        }

        problem = configuration.ContextCodecs.Count > 0
            ? $"no source-generated codec is registered for '{payloadType.FullName}' (checked the static registry " +
              "and the explicitly supplied JsonSerializerContext instances)"
            : "no source-generated codec is registered for '" + payloadType.FullName +
              "'; the owning assembly must import the CrestCreates.JsonContracts.Build toolchain";
        return null;
    }

    private sealed record ResolvedSubscription(
        GeneratedEventDescriptor Descriptor,
        string ContributorOrigin,
        RabbitMqHandlerBinding Handler,
        string Exchange,
        string Queue,
        string RoutingKey,
        int PrefetchCount);
}

/// <summary>Frozen RabbitMQ binding registry (see IRabbitMqBindingRegistry).</summary>
public sealed class RabbitMqBindingRegistry : IRabbitMqBindingRegistry
{
    private readonly FrozenDictionary<Type, RabbitMqPublishBinding> _publishByType;
    private readonly FrozenDictionary<(string EventId, int EventVersion), RabbitMqPublishBinding> _publishByWire;
    private readonly FrozenDictionary<string, RabbitMqPublishBinding> _legacyAliases;

    internal RabbitMqBindingRegistry(
        bool publisherOnly,
        IReadOnlyList<RabbitMqPublishBinding> publishBindings,
        IReadOnlyList<RabbitMqQueuePlan> queuePlans,
        IReadOnlyDictionary<string, RabbitMqPublishBinding> legacyAliases)
    {
        PublisherOnly = publisherOnly;
        QueuePlans = queuePlans;
        _publishByType = publishBindings.ToFrozenDictionary(b => b.Descriptor.PayloadType);
        _publishByWire = publishBindings.ToFrozenDictionary(b => (b.Descriptor.Id, b.Descriptor.Version));
        _legacyAliases = legacyAliases.ToFrozenDictionary(StringComparer.Ordinal);
        PublishableEventTypes = _publishByType.Keys.ToList();
    }

    public bool PublisherOnly { get; }

    public IReadOnlyList<RabbitMqQueuePlan> QueuePlans { get; }

    public IReadOnlyCollection<Type> PublishableEventTypes { get; }

    public bool TryGetPublishBinding(Type eventType, out RabbitMqPublishBinding? binding)
        => _publishByType.TryGetValue(eventType, out binding);

    public RabbitMqWireResolution ResolveWire(string eventType, string? eventId, int? eventVersion)
    {
        if (eventId is null && eventVersion is null)
        {
            if (_legacyAliases.TryGetValue(eventType, out var aliased))
            {
                return new RabbitMqWireResolution(aliased, null);
            }

            return new RabbitMqWireResolution(
                null,
                $"legacy message carries unknown wire event identity '{eventType}' and no explicit legacy alias " +
                "maps it to a canonical event");
        }

        if (eventId is null || eventVersion is null)
        {
            return new RabbitMqWireResolution(
                null,
                "incomplete wire identity: both EventId and EventVersion are required on new-format envelopes " +
                "(messages with only one of the two fields are neither valid new format nor an explicit legacy alias)");
        }

        if (!_publishByWire.TryGetValue((eventId, eventVersion.Value), out var binding))
        {
            return new RabbitMqWireResolution(
                null,
                $"unknown wire identity (EventId '{eventId}', v{eventVersion.Value}); no fallback resolution is performed");
        }

        if (!string.Equals(binding.Descriptor.Name, eventType, StringComparison.Ordinal) &&
            !(_legacyAliases.TryGetValue(eventType, out var aliasTarget) &&
              string.Equals(aliasTarget.Descriptor.Id, binding.Descriptor.Id, StringComparison.Ordinal) &&
              aliasTarget.Descriptor.Version == binding.Descriptor.Version))
        {
            return new RabbitMqWireResolution(
                null,
                $"wire identity (EventId '{eventId}', v{eventVersion.Value}) resolves to '{binding.Descriptor.Name}' " +
                $"but the envelope EventType is '{eventType}'; identity fields are inconsistent");
        }

        return new RabbitMqWireResolution(binding, null);
    }
}
