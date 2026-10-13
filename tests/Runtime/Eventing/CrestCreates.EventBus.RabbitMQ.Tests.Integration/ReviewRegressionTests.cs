using System.Reflection;
using System.Text.Json.Serialization;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.RabbitMq.TestContractsA;
using CrestCreates.RabbitMq.TestHandlersA;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public sealed class ReviewRegressionTests(ITestOutputHelper output) : RabbitMqIntegrationTestBase(output)
{
    // Adapted from the review probe after R1-RMQ-05: the process-global EventJsonCodecRegistry was
    // removed; codecs are per-host service contributions (EventJsonCodecRegistration) registered by the
    // owning assembly's activation. The scenario is unchanged: Host B without its own contribution
    // must not observe Host A's codec.
    [Fact]
    public void Host_B_without_own_codec_contribution_must_not_use_Host_A_codec()
    {
        using var hostA = NewCodecHost(includeCodec: true);
        Freeze(hostA);
        using var hostB = NewCodecHost(includeCodec: false);
        FluentActions.Invoking(() => Freeze(hostB)).Should().Throw<RabbitMqCompositionException>(
            "Host B supplies neither an owning codec contribution nor an explicit typed codec");
    }

    [Fact]
    public void Codec_contribution_registration_order_and_idempotent_reactivation_are_host_local()
    {
        // Registration after AddRabbitMqEventBus must still be effective (freeze resolves later).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventDescriptorProvider, ReviewDescriptorProvider>();
        services.AddRabbitMqEventBus(builder: b => b.PublisherOnly().RequirePublishableEvent<ReviewWirePayload>());
        var registration = new EventJsonCodecRegistration
        {
            PayloadType = typeof(ReviewWirePayload),
            CodecFactory = () => ReviewJsonContext.Default.ReviewWirePayload,
            Origin = "ReviewContracts",
        };
        services.AddSingleton(registration);
        services.AddSingleton(registration); // duplicate activation of the same origin is idempotent
        using var provider = services.BuildServiceProvider();

        FluentActions.Invoking(() => Freeze(provider)).Should().NotThrow();
    }

    [Fact]
    public void Conflicting_codec_origins_for_one_payload_must_fail_freeze()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventDescriptorProvider, ReviewDescriptorProvider>();
        services.AddSingleton(new EventJsonCodecRegistration
        {
            PayloadType = typeof(ReviewWirePayload),
            CodecFactory = () => ReviewJsonContext.Default.ReviewWirePayload,
            Origin = "OriginA",
        });
        services.AddSingleton(new EventJsonCodecRegistration
        {
            PayloadType = typeof(ReviewWirePayload),
            CodecFactory = () => ReviewJsonContext.Default.ReviewWirePayload,
            Origin = "OriginB",
        });
        services.AddRabbitMqEventBus(builder: b => b.PublisherOnly().RequirePublishableEvent<ReviewWirePayload>());
        using var provider = services.BuildServiceProvider();

        var failure = FluentActions.Invoking(() => Freeze(provider))
            .Should().Throw<RabbitMqCompositionException>().Subject.Single();
        failure.Violations.Should().Contain(v => v.Contains("multiple origins", StringComparison.Ordinal));
    }

    private static ServiceProvider NewCodecHost(bool includeCodec)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventDescriptorProvider, ReviewDescriptorProvider>();
        if (includeCodec)
        {
            services.AddSingleton(new EventJsonCodecRegistration
            {
                PayloadType = typeof(ReviewWirePayload),
                CodecFactory = () => ReviewJsonContext.Default.ReviewWirePayload,
                Origin = "HostA",
            });
        }

        services.AddRabbitMqEventBus(builder: b => b.PublisherOnly().RequirePublishableEvent<ReviewWirePayload>());
        return services.BuildServiceProvider();
    }

    private static IRabbitMqBindingRegistry Freeze(ServiceProvider provider)
    {
        provider.GetRequiredService<EventRegistry>().Build(provider.GetServices<IEventDescriptorProvider>());
        return provider.GetRequiredService<IRabbitMqBindingRegistry>();
    }

    [Fact]
    public async Task Adding_RabbitMQ_must_not_require_governance_for_plain_LocalEvent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestLocalEventDispatch(b => b.Declare<ReviewLocalEvent>());
        services.AddScoped<ILocalEventHandler<ReviewLocalEvent>, ReviewLocalHandler>();
        services.AddRabbitMqEventBus(builder: b => b.PublisherOnly());
        await using var provider = services.BuildServiceProvider();
        Freeze(provider);
        await using var scope = provider.CreateAsyncScope();
        await FluentActions.Awaiting(() => scope.ServiceProvider.GetRequiredService<ILocalEventBus>()
            .PublishAsync(new ReviewLocalEvent())).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Missing_Handler_DI_registration_must_fail_composition_before_broker_IO()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsA.EventRegistrations.AddEventDescriptors(services);
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestHandlersA.RabbitMqSubscriptions.AddRabbitMqSubscriptions(services);
        services.RemoveAll<OrderPlacedHandler>();
        services.AddRabbitMqEventBus(builder: b => b.RequireSubscription<OrderPlacedHandler>());
        await using var provider = services.BuildServiceProvider();
        var composition = provider.GetServices<IHostedService>()
            .Single(s => s.GetType().Name == "RabbitMqCompositionHostedService");
        await FluentActions.Awaiting(() => composition.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<RabbitMqCompositionException>();
    }

    [Fact]
    public async Task Missing_later_Handler_must_not_execute_earlier_Handler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<RabbitMqScopedProbe>();
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsA.EventRegistrations.AddEventDescriptors(builder.Services);
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestHandlersA.RabbitMqSubscriptions.AddRabbitMqSubscriptions(builder.Services);
        builder.Services.RemoveAll<OrderPlacedHandler>();
        builder.Services.AddRabbitMqEventBus(o =>
        {
            o.VirtualHost = VHost;
            o.DefaultExchange = "itest127.events";
            o.DeadLetterExchange = "itest127.dlx";
            o.RetryCount = 0;
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            (await WaitUntilAsync(() => QueueExistsAsync(VHost, QueueNames.Orders), TimeSpan.FromSeconds(10))).Should().BeTrue();
            await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(new OrderPlacedEvent { OrderId = "missing-handler" });
            (await WaitUntilAsync(async () => await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages") == 1,
                TimeSpan.FromSeconds(10))).Should().BeTrue();
            RabbitMqTestHooks.CallLog.Should().BeEmpty("missing required handler must be checked before any business calls");
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Envelope_with_JSON_null_Payload_field_must_reach_DLQ()
    {
        await StartHostAsync();
        var id = GeneratedEventDescriptor.GenerateId("orders.order-placed");
        var raw = "{\"eventType\":\"orders.order-placed\",\"eventId\":\"" + id + "\",\"eventVersion\":1,\"payload\":null,\"headers\":{}}";
        await PublishRawAsync(VHost, QueueNames.Orders, raw, messageId: "null-envelope-payload");
        var reached = await WaitUntilAsync(async () => await GetQueueDepthAsync(VHost, QueueNames.Orders + ".dlq", "messages") == 1,
            TimeSpan.FromSeconds(10));
        var unacked = await GetQueueDepthAsync(VHost, QueueNames.Orders, "messages_unacknowledged");
        Output.WriteLine($"null payload reached DLQ={reached}; unacked={unacked}");
        reached.Should().BeTrue("an invalid envelope must not occupy an unacknowledged delivery indefinitely");
    }

    [Fact]
    public async Task Publish_during_confirmed_recovery_window_must_preserve_consumer()
    {
        var host = await StartHostAsync();
        var pool = host.Services.GetRequiredService<RabbitMqConnectionPool>();
        var connectionField = typeof(RabbitMqConnectionPool).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var old = (IConnection)connectionField.GetValue(pool)!;
        (await WaitUntilAsync(async () => (await ManagementJsonAsync(HttpMethod.Get, "connections"))
            .EnumerateArray().Any(c => c.TryGetProperty("vhost", out var v) && v.GetString() == VHost),
            TimeSpan.FromSeconds(10))).Should().BeTrue("the target connection must actually appear in management before deletion");
        await CloseAllConnectionsAsync(VHost);
        (await WaitUntilAsync(() => Task.FromResult(!old.IsOpen), TimeSpan.FromSeconds(2))).Should().BeTrue("a real drop is required");
        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(new OrderPlacedEvent { OrderId = "during-recovery" });
        var sameConnection = ReferenceEquals(old, connectionField.GetValue(pool));
        var consumed = await WaitUntilAsync(() => Task.FromResult(RabbitMqTestHooks.CallLog.Any(x => x.StartsWith("order:during-recovery:"))),
            TimeSpan.FromSeconds(12));
        Output.WriteLine($"original connection retained={sameConnection}; consumed={consumed}");
        consumed.Should().BeTrue("publishing during recovery must not dispose the consumer's connection and leave no consumer");
    }
}

public sealed class ReviewLocalEvent : ILocalEvent;
public sealed class ReviewLocalHandler : ILocalEventHandler<ReviewLocalEvent>
{
    public Task HandleAsync(ReviewLocalEvent e, CancellationToken ct = default) => Task.CompletedTask;
}
public sealed class ReviewWirePayload { public string Value { get; set; } = "value"; }
[JsonSerializable(typeof(ReviewWirePayload))]
internal partial class ReviewJsonContext : JsonSerializerContext;
public sealed class ReviewDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() =>
    [new GeneratedEventDescriptor { Id = "review_wire", Name = "review.wire", Version = 1,
        PayloadType = typeof(ReviewWirePayload), Scope = EventScope.Integration, State = DescriptorState.Active }];
}
