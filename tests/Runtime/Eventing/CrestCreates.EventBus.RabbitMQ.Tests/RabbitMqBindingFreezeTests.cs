using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Registration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

/// <summary>
/// Freeze matrix for the RabbitMQ binding registry: canonical reference resolution, codec availability,
/// alias integrity, queue plan conflicts, required manifests, publisher-only mode, registration-order
/// independence and fail-before-broker-I/O. Everything runs without a broker.
/// </summary>
public class RabbitMqBindingFreezeTests
{
    private static ServiceProvider BuildProvider(
        Action<RabbitMqEventBusBuilder>? builder = null,
        Action<RabbitMqOptions>? options = null,
        params Type[] descriptorProviders)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPerHostCodecs();
        services.AddScoped<TestHandler>();
        foreach (var providerType in descriptorProviders)
        {
            services.AddSingleton(typeof(IEventDescriptorProvider), providerType);
        }

        services.AddRabbitMqEventBus(
            configure: options ?? (_ => { }),
            builder: builder);

        return services.BuildServiceProvider();
    }

    private static IRabbitMqBindingRegistry Freeze(ServiceProvider provider)
    {
        var registry = provider.GetRequiredService<EventRegistry>();
        registry.Build(provider.GetServices<IEventDescriptorProvider>());
        return provider.GetRequiredService<IRabbitMqBindingRegistry>();
    }

    private static RabbitMqCompositionException CaptureFreezeFailure(ServiceProvider provider)
        => FluentActions.Invoking(() => Freeze(provider)).Should().Throw<RabbitMqCompositionException>().Subject.Single();

    // ── Positive paths ──

    [Fact]
    public void Freeze_Builds_publish_index_and_queue_plan_from_canonical_identity()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-orders");

        using var provider = BuildProvider(
            builder => builder
                .AddContributor(contributor)
                .RequirePublishableEvent<OrderPlacedPayload>()
                .RequireSubscription<TestHandler>(),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var registry = Freeze(provider);

        registry.PublisherOnly.Should().BeFalse();
        registry.TryGetPublishBinding(typeof(OrderPlacedPayload), out var binding).Should().BeTrue();
        binding!.Descriptor.Name.Should().Be("orders.order-placed");
        binding.Descriptor.Id.Should().Be(GeneratedEventDescriptor.GenerateId("orders.order-placed"));
        binding.RoutingKey.Should().Be("orders.order-placed");
        binding.Codec.Type.Should().Be(typeof(OrderPlacedPayload));

        var plan = registry.QueuePlans.Should().ContainSingle().Subject;
        plan.Queue.Should().Be("q-orders");
        plan.PrefetchCount.Should().Be(10);
        plan.Declarations.Should().ContainSingle()
            .Which.Should().Be(new RabbitMqQueueDeclaration(
                "crestcreates.events",
                "orders.order-placed",
                new RabbitMqWireIdentity(binding.Descriptor.Id, 1)));
        plan.HandlersByIdentity.Keys.Should().ContainSingle()
            .Which.Should().Be(new RabbitMqWireIdentity(binding.Descriptor.Id, 1));
    }

    [Fact]
    public void Freeze_Default_queue_name_follows_canonical_name_rule()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload));

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        Freeze(provider).QueuePlans.Should().ContainSingle()
            .Which.Queue.Should().Be("crestcreates.orders.order-placed");
    }

    [Fact]
    public void Freeze_Resolves_wire_identity_and_rejects_incomplete_or_unknown()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload));

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var registry = Freeze(provider);
        var id = GeneratedEventDescriptor.GenerateId("orders.order-placed");

        registry.ResolveWire("orders.order-placed", id, 1).Success.Should().BeTrue();
        registry.ResolveWire("orders.order-placed", id, 2).FailureReason.Should().Contain("unknown wire identity");
        registry.ResolveWire("billing.invoice-issued", id, 1).FailureReason.Should().Contain("inconsistent");
        registry.ResolveWire("orders.order-placed", id, null).FailureReason.Should().Contain("incomplete wire identity");
        registry.ResolveWire("orders.order-placed", null, 1).FailureReason.Should().Contain("incomplete wire identity");
        registry.ResolveWire("orders.order-placed", null, null).FailureReason.Should().Contain("legacy alias");
        registry.ResolveWire("billing.invoice-issued", null, null).FailureReason.Should().Contain("legacy alias");
    }

    [Fact]
    public void Freeze_Legacy_alias_maps_only_through_explicit_static_declaration()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload));

        using var provider = BuildProvider(
            builder => builder
                .AddContributor(contributor)
                .AddLegacyWireAlias("OrderPlacedEvent", "orders.order-placed", 1)
                .AddLegacyWireAlias("MyApp.Orders.OrderPlacedEvent", "orders.order-placed", 1),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var registry = Freeze(provider);
        var id = GeneratedEventDescriptor.GenerateId("orders.order-placed");

        var legacy = registry.ResolveWire("OrderPlacedEvent", null, null);
        legacy.Success.Should().BeTrue();
        legacy.Binding!.Descriptor.Id.Should().Be(id);

        var fullyQualified = registry.ResolveWire("MyApp.Orders.OrderPlacedEvent", null, null);
        fullyQualified.Binding!.Descriptor.Id.Should().Be(id);

        // A declared alias may also appear as EventType on a new-format envelope; the identity fields
        // stay authoritative for version resolution.
        registry.ResolveWire("OrderPlacedEvent", id, 1).Success.Should().BeTrue();
        registry.ResolveWire("OrderPlacedEvent", id, 2).FailureReason.Should().Contain("unknown wire identity");
    }

    [Fact]
    public void Freeze_Ordering_is_stable_across_registration_order()
    {
        var beta = new TestContributor("handlers.beta")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-shared");
        var alpha = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-shared");

        using var betaFirst = BuildProvider(
            builder => builder.AddContributor(beta).AddContributor(alpha),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);
        using var alphaFirst = BuildProvider(
            builder => builder.AddContributor(alpha).AddContributor(beta),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var planBetaFirst = Freeze(betaFirst).QueuePlans.Single();
        var planAlphaFirst = Freeze(alphaFirst).QueuePlans.Single();

        var orderBetaFirst = planBetaFirst.HandlersByIdentity.Single().Value.Select(h => h.Origin).ToList();
        var orderAlphaFirst = planAlphaFirst.HandlersByIdentity.Single().Value.Select(h => h.Origin).ToList();

        orderBetaFirst.Should().Equal("handlers.alpha", "handlers.beta");
        orderAlphaFirst.Should().Equal(orderBetaFirst);
    }

    [Fact]
    public void Freeze_PublisherOnly_allows_zero_subscriptions_but_requires_codec_for_published_events()
    {
        using var provider = BuildProvider(
            builder => builder.PublisherOnly().RequirePublishableEvent<OrderPlacedPayload>(),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var registry = Freeze(provider);
        registry.PublisherOnly.Should().BeTrue();
        registry.QueuePlans.Should().BeEmpty();
        registry.TryGetPublishBinding(typeof(OrderPlacedPayload), out _).Should().BeTrue();
    }

    [Fact]
    public void Freeze_Manual_typed_declaration_supplies_codec_and_routing_override()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("third.party-created", typeof(ThirdPartyPayload));

        using var provider = BuildProvider(
            builder => builder
                .AddContributor(contributor)
                .AddPublishedEvent(FreezeTestJsonContext.Default.ThirdPartyPayload, routingKey: "legacy.third_created"),
            descriptorProviders: [typeof(ThirdPartyDescriptorProvider)]);

        var registry = Freeze(provider);
        registry.TryGetPublishBinding(typeof(ThirdPartyPayload), out var binding).Should().BeTrue();
        binding!.RoutingKey.Should().Be("legacy.third_created");
        binding.Codec.Type.Should().Be(typeof(ThirdPartyPayload));
    }

    [Fact]
    public void Freeze_Manual_and_static_codecs_join_the_same_freeze()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("third.party-created", typeof(ThirdPartyPayload));

        using var provider = BuildProvider(
            builder => builder
                .AddContributor(contributor)
                .AddPublishedEvent(FreezeTestJsonContext.Default.ThirdPartyPayload, routingKey: null)
                .AddPublishedEvent(FreezeTestJsonContext.Default.InvoiceIssuedPayload, routingKey: null),
            descriptorProviders: [typeof(ThirdPartyDescriptorProvider), typeof(InvoiceIssuedDescriptorProvider)]);

        // The InvoiceIssued event resolves through its static codec; the ThirdParty manual codec is valid.
        var registry = Freeze(provider);
        registry.TryGetPublishBinding(typeof(ThirdPartyPayload), out _).Should().BeTrue();
        registry.TryGetPublishBinding(typeof(InvoiceIssuedPayload), out _).Should().BeTrue();
    }

    // ── Negative paths ──

    [Fact]
    public void Freeze_Rejects_zero_subscriptions_without_publisher_only()
    {
        using var provider = BuildProvider(descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("No subscriptions are declared"));
    }

    [Fact]
    public void Freeze_Rejects_subscription_without_descriptor()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.unknown", typeof(OrderPlacedPayload));

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("has no Active descriptor"));
    }

    [Fact]
    public void Freeze_Rejects_subscription_with_mismatched_explicit_version()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), version: 3);

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("has no matching descriptor version"));
    }

    [Fact]
    public void Freeze_Rejects_non_integration_scope_for_subscriptions()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("local.only", typeof(LocalScopedPayload));

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(LocalScopedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("RabbitMQ consumes Integration events only"));
    }

    [Fact]
    public void Freeze_Rejects_payload_type_mismatch()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(InvoiceIssuedPayload));

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("canonical descriptor payload"));
    }

    [Fact]
    public void Freeze_Rejects_subscription_when_codec_missing()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("events.no-codec", typeof(NoCodecPayload));

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(NoCodecDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v =>
            v.Contains("has no frozen codec") && v.Contains(nameof(NoCodecPayload)));
    }

    [Fact]
    public void Freeze_Rejects_manual_declaration_without_descriptor()
    {
        using var provider = BuildProvider(
            builder => builder.PublisherOnly().AddPublishedEvent(FreezeTestJsonContext.Default.ThirdPartyPayload),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("has no Active event descriptor"));
    }

    [Fact]
    public void Freeze_Rejects_duplicate_manual_declarations()
    {
        using var provider = BuildProvider(
            builder => builder.PublisherOnly()
                .AddPublishedEvent(FreezeTestJsonContext.Default.OrderPlacedPayload)
                .AddPublishedEvent(FreezeTestJsonContext.Default.OrderPlacedPayload, routingKey: "orders.order_placed"),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("Duplicate manual publish declaration"));
    }

    [Fact]
    public void Freeze_Rejects_alias_shadowing_canonical_name()
    {
        using var provider = BuildProvider(
            builder => builder.PublisherOnly()
                .AddLegacyWireAlias("orders.order-placed", "orders.order-placed", 1),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("collides with an existing canonical event name"));
    }

    [Fact]
    public void Freeze_Rejects_alias_with_unknown_target()
    {
        using var provider = BuildProvider(
            builder => builder.PublisherOnly().AddLegacyWireAlias("OrderPlacedEvent", "orders.order-placed", 2),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("is not an Active Integration event"));
    }

    [Fact]
    public void Freeze_Rejects_duplicate_aliases()
    {
        using var provider = BuildProvider(
            builder => builder.PublisherOnly()
                .AddLegacyWireAlias("OrderPlacedEvent", "orders.order-placed", 1)
                .AddLegacyWireAlias("OrderPlacedEvent", "orders.order-placed", 1),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("Duplicate legacy alias"));
    }

    [Fact]
    public void Freeze_Rejects_conflicting_prefetch_on_same_queue()
    {
        var first = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-shared", prefetchCount: 5);
        var second = new TestContributor("handlers.beta")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-shared", prefetchCount: 9);

        using var provider = BuildProvider(
            builder => builder.AddContributor(first).AddContributor(second),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("conflicting prefetch counts"));
    }

    [Fact]
    public void Freeze_Rejects_out_of_range_prefetch()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), prefetchCount: 70000);

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("valid range is 1..65535"));
    }

    [Fact]
    public void Freeze_Rejects_missing_required_handler()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload));

        using var provider = BuildProvider(
            builder => builder
                .AddContributor(contributor)
                .RequirePublishableEvent<InvoiceIssuedPayload>()
                .RequireSubscription<MissingHandler>(),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider), typeof(InvoiceIssuedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("MissingHandler"));
    }

    [Fact]
    public void Freeze_Rejects_same_origin_codec_conflicts_in_both_registration_orders()
    {
        static RabbitMqCompositionException Capture(bool goodFirst)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IEventDescriptorProvider, OrderPlacedDescriptorProvider>();
            var good = new EventJsonCodecRegistration
            {
                PayloadType = typeof(OrderPlacedPayload),
                CodecFactory = static () => FreezeTestJsonContext.Default.OrderPlacedPayload,
                Origin = "Same.Owner",
            };
            var wrong = new EventJsonCodecRegistration
            {
                PayloadType = typeof(OrderPlacedPayload),
                CodecFactory = static () => FreezeTestJsonContext.Default.InvoiceIssuedPayload,
                Origin = "Same.Owner",
            };
            services.AddSingleton(goodFirst ? good : wrong);
            services.AddSingleton(goodFirst ? wrong : good);
            services.AddRabbitMqEventBus(builder: b => b.PublisherOnly().RequirePublishableEvent<OrderPlacedPayload>());
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<EventRegistry>().Build(provider.GetServices<IEventDescriptorProvider>());
            return FluentActions.Invoking(() => provider.GetRequiredService<IRabbitMqBindingRegistry>())
                .Should().Throw<RabbitMqCompositionException>().Subject.Single();
        }

        Capture(goodFirst: true).Violations.Should().ContainSingle(v =>
            v.Contains("is invalid", StringComparison.Ordinal));
        Capture(goodFirst: false).Violations.Should().ContainSingle(v =>
            v.Contains("is invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void Freeze_Rejects_required_handler_that_is_not_registered_in_di()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), handlerType: typeof(UnregisteredHandler));

        using var provider = BuildProvider(
            builder => builder
                .AddContributor(contributor)
                .RequireSubscription<UnregisteredHandler>(),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v =>
            v.Contains("declared but not registered", StringComparison.Ordinal) &&
            v.Contains(nameof(UnregisteredHandler), StringComparison.Ordinal));
    }

    [Fact]
    public void Freeze_Allows_unregistered_non_required_handler_because_delivery_pre_resolution_guards_it()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), handlerType: typeof(UnregisteredHandler));

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        Freeze(provider).QueuePlans.Should().ContainSingle();
    }

    [Fact]
    public void Freeze_Rejects_required_publishable_event_without_codec()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload));

        using var provider = BuildProvider(
            builder => builder
                .AddContributor(contributor)
                .RequirePublishableEvent<NoCodecPayload>(),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider), typeof(NoCodecDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("has no frozen codec"));
    }

    [Fact]
    public void Freeze_Rejects_duplicate_subscription_bindings_from_distinct_contributors()
    {
        var first = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-orders");
        var second = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-orders");

        using var provider = BuildProvider(
            builder => builder.AddContributor(first).AddContributor(second),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var failure = CaptureFreezeFailure(provider);
        failure.Violations.Should().ContainSingle(v => v.Contains("Duplicate subscription binding"));
    }

    [Fact]
    public void Freeze_Same_instance_contributor_added_twice_is_idempotent()
    {
        var contributor = new TestContributor("handlers.alpha")
            .AddSubscription("orders.order-placed", typeof(OrderPlacedPayload), queue: "q-orders");

        using var provider = BuildProvider(
            builder => builder.AddContributor(contributor).AddContributor(contributor),
            descriptorProviders: [typeof(OrderPlacedDescriptorProvider)]);

        var registry = Freeze(provider);
        registry.QueuePlans.Should().ContainSingle();
        registry.QueuePlans.Single().HandlersByIdentity.Single().Value.Should().ContainSingle();
    }

    [Fact]
    public void Freeze_Rejects_before_broker_io_when_event_registry_is_not_built()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventDescriptorProvider, OrderPlacedDescriptorProvider>();
        services.AddRabbitMqEventBus();
        using var provider = services.BuildServiceProvider();

        // Deliberately never build the EventRegistry: freeze must fail with a readable diagnostic
        // instead of touching any broker endpoint.
        FluentActions.Invoking(() => provider.GetRequiredService<IRabbitMqBindingRegistry>())
            .Should().Throw<RabbitMqCompositionException>()
            .Which.Violations.Should().ContainSingle(v => v.Contains("EventRegistry is not built"));
    }

    private sealed class MissingHandler;

    private sealed class UnregisteredHandler;
}
