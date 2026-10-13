using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.RabbitMq.TestContractsA;
using CrestCreates.RabbitMq.TestHandlersA;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

/// <summary>
/// Host composition contracts: freeze failures fail host startup deterministically before any broker
/// I/O, the module entry forwards to the same single entry with configuration binding, and a
/// publisher-only host starts with zero queues and no consumer.
/// </summary>
public class RabbitMqHostCompositionTests : RabbitMqIntegrationTestBase
{
    public RabbitMqHostCompositionTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Freeze_failure_fails_host_start_without_touching_the_broker()
    {
        // Broken composition: the handler subscribes an event whose contract assembly was never
        // activated. The broker endpoint is deliberately unreachable to prove that no connection is
        // attempted before freeze completes.
        var host = BuildHost(
            contractA: false,
            contractB: false,
            handlersA: true,
            handlersB: false,
            options: o => o.Port = 1);

        var failure = await FluentActions.Awaiting(() => host.StartAsync())
            .Should().ThrowAsync<RabbitMqCompositionException>();

        failure.Which.Violations.Should().Contain(v => v.Contains("orders.order-placed", StringComparison.Ordinal)
            && v.Contains("no Active descriptor", StringComparison.Ordinal));
        host.Dispose();
    }

    [Fact]
    public async Task Module_entry_forwards_to_the_single_entry_and_binds_configuration()
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Services.AddScoped<RabbitMqScopedProbe>();
        var services = hostBuilder.Services;

        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsA.EventRegistrations
            .AddEventDescriptors(services);
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestHandlersA.RabbitMqSubscriptions
            .AddRabbitMqSubscriptions(services);

        var effective = DefaultOptions(options => options.RetryDelaySeconds = 1);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = effective.HostName,
                ["RabbitMQ:Port"] = effective.Port.ToString(),
                ["RabbitMQ:VirtualHost"] = effective.VirtualHost,
                ["RabbitMQ:UserName"] = effective.UserName,
                ["RabbitMQ:Password"] = effective.Password,
                ["RabbitMQ:DefaultExchange"] = effective.DefaultExchange,
                ["RabbitMQ:DeadLetterExchange"] = effective.DeadLetterExchange,
                ["RabbitMQ:RetryDelaySeconds"] = effective.RetryDelaySeconds.ToString(),
                ["RabbitMQ:MaxChannels"] = effective.MaxChannels.ToString(),
            })
            .Build();

        new RabbitMqEventBusModule(configuration).OnConfigureServices(services);

        using var host = hostBuilder.Build();
        await host.StartAsync();

        (await WaitUntilAsync(
            async () => await QueueExistsAsync(VHost, QueueNames.Orders),
            TimeSpan.FromSeconds(15))).Should().BeTrue("the module host must declare the consumer topology");

        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(
            new OrderPlacedEvent { OrderId = "module-1", Amount = 1m });

        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:module-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue("the module entry must consume through the same mainline");

        var applied = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            CrestCreates.EventBus.RabbitMQ.Options.RabbitMqOptions>>().Value;
        applied.VirtualHost.Should().Be(VHost);

        await host.StopAsync();
    }

    [Fact]
    public async Task Publisher_only_host_starts_with_zero_queues_and_no_consumer()
    {
        var host = await StartHostAsync(
            builder => builder.PublisherOnly(),
            contractB: false,
            handlersA: false,
            handlersB: false,
            waitForQueues: []);

        var queues = await GetQueueNamesAsync(VHost);
        queues.Should().BeEmpty("a publisher-only host must not declare or consume any queue");
    }
}
