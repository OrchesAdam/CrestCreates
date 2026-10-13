using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.RabbitMQ;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.RabbitMq.TestContractsA;
using CrestCreates.RabbitMq.TestContractsB;
using CrestCreates.RabbitMq.TestHandlersA;
using AOrderPlaced = CrestCreates.RabbitMq.TestContractsA.OrderPlacedEvent;
using BOrderPlaced = CrestCreates.RabbitMq.TestContractsB.OrderPlacedEvent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.RabbitMQ.AotHost;

/// <summary>
/// Native publish/link/run gate for the static-binding RabbitMQ mainline: the native binary must run
/// the official AddRabbitMqEventBus composition with the generated contract/handler activations
/// against a real broker, deliver typed payloads across assemblies (including a same-short-name
/// collision), refuse malformed/unknown messages onto the dead-letter path, honour retry budgets and
/// report typed publish failures — then print the sentinel. Credentials come from the environment and
/// are never logged.
/// </summary>
internal static class Program
{
    private static string HostName => Environment.GetEnvironmentVariable("CREST127_HOST") ?? "localhost";
    private static int AmqpPort => int.TryParse(Environment.GetEnvironmentVariable("CREST127_PORT"), out var port) ? port : 5672;
    private static int ManagementPort => int.TryParse(Environment.GetEnvironmentVariable("CREST127_MGMT_PORT"), out var port) ? port : 15672;
    private static string UserName => Environment.GetEnvironmentVariable("CREST127_USER") ?? "guest";
    private static string Password => Environment.GetEnvironmentVariable("CREST127_PASSWORD") ?? "guest";
    private static string VHost => Environment.GetEnvironmentVariable("CREST127_VHOST")
        ?? throw new InvalidOperationException("CREST127_VHOST must be provided by the harness.");

    private static async Task<int> Main()
    {
        var failures = new List<string>();

        await Run("typed-cross-assembly", TypedCrossAssemblyAsync, failures);
        await Run("refusals-to-dead-letter", RefusalsAsync, failures);
        await Run("legacy-alias", LegacyAliasAsync, failures);
        await Run("retry-then-success", RetryThenSuccessAsync, failures);
        await Run("budget-exhaustion-to-dlq", ExhaustionAsync, failures);
        await Run("unroutable-publish-kind", UnroutableAsync, failures);

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine($"FAIL: {failure}");
            }

            return 1;
        }

        Console.WriteLine("CRESTCREATES_RABBITMQ_STATIC_BINDINGS_NATIVE_OK");
        return 0;
    }

    private static async Task Run(string name, Func<Task> scenario, List<string> failures)
    {
        try
        {
            RabbitMqTestHooks.Reset();
            RabbitMqTestHooksB.Reset();
            await scenario();
            Console.WriteLine($"{name}:PASS");
        }
        catch (Exception exception)
        {
            failures.Add($"{name}: {exception}");
        }
    }

    private static IHost BuildHost(
        Action<RabbitMqEventBusBuilder>? builder = null,
        Action<RabbitMqOptions>? options = null)
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        var services = hostBuilder.Services;

        services.AddScoped<RabbitMqScopedProbe>();
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsA.EventRegistrations.AddEventDescriptors(services);
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsB.EventRegistrations.AddEventDescriptors(services);
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestHandlersA.RabbitMqSubscriptions.AddRabbitMqSubscriptions(services);
        CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsB.RabbitMqSubscriptions.AddRabbitMqSubscriptions(services);

        var effective = new RabbitMqOptions
        {
            HostName = HostName,
            Port = AmqpPort,
            UserName = UserName,
            Password = Password,
            VirtualHost = VHost,
            MaxChannels = 4,
            RetryCount = 0,
            RetryDelaySeconds = 0,
            DeadLetterExchange = "aot127.dlx",
            DefaultExchange = "aot127.events",
            PublisherConfirmTimeoutSeconds = 15,
        };
        options?.Invoke(effective);

        services.AddRabbitMqEventBus(configure: o =>
        {
            o.HostName = effective.HostName;
            o.Port = effective.Port;
            o.UserName = effective.UserName;
            o.Password = effective.Password;
            o.VirtualHost = effective.VirtualHost;
            o.MaxChannels = effective.MaxChannels;
            o.RetryCount = effective.RetryCount;
            o.RetryDelaySeconds = effective.RetryDelaySeconds;
            o.DeadLetterExchange = effective.DeadLetterExchange;
            o.DefaultExchange = effective.DefaultExchange;
            o.PublisherConfirmTimeoutSeconds = effective.PublisherConfirmTimeoutSeconds;
        }, builder: b =>
        {
            builder?.Invoke(b);
            b.AddPublishedEvent<BOrderPlaced>(
                routingKey: InvoiceIssuedHandler.RoutingKey);
        });

        return hostBuilder.Build();
    }

    private static async Task StartHostAsync(IHost host)
    {
        await host.StartAsync();
        // The consumer declares topology asynchronously; publisher-side retries below cover the gap.
    }

    private static async Task PublishWithTopologyRetryAsync(RabbitMqEventBus bus, IDomainEvent @event)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await bus.PublishAsync(@event);
                return;
            }
            catch (RabbitMqPublishException)
            {
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException("Topology never became ready for publish.");
    }

    private static Task WaitAsync(Func<bool> condition, string message, int seconds = 15)
        => WaitAsync(() => Task.FromResult(condition()), message, seconds);

    private static async Task WaitAsync(Func<Task<bool>> condition, string message, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Check(await condition(), message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Fixture assertion failed: " + message);
        }
    }

    private static async Task<long> QueueDepthAsync(string queue)
    {
        using var client = CreateManagementClient();
        using var response = await client.GetAsync($"queues/{VHost}/{queue}");
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("messages", out var messages) ? messages.GetInt64() : 0;
    }

    private static HttpClient CreateManagementClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{HostName}:{ManagementPort}/api/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{UserName}:{Password}")));
        return client;
    }

    private static async Task PublishRawAsync(string queue, string payload)
    {
        await using var connection = await new global::RabbitMQ.Client.ConnectionFactory
        {
            HostName = HostName,
            Port = AmqpPort,
            UserName = UserName,
            Password = Password,
            VirtualHost = VHost,
        }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: true,
            basicProperties: new global::RabbitMQ.Client.BasicProperties(),
            body: Encoding.UTF8.GetBytes(payload));
    }

    private static async Task TypedCrossAssemblyAsync()
    {
        using var host = BuildHost();
        await StartHostAsync(host);
        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();

        AOrderPlaced? received = null;
        RabbitMqTestHooks.OnOrderPlaced = (evt, _) =>
        {
            received = evt;
            return Task.CompletedTask;
        };

        await PublishWithTopologyRetryAsync(bus, new AOrderPlaced
        {
            OrderId = "aot-a-1",
            Amount = 19.95m,
            Tags = ["native", "aot"],
        });
        await WaitAsync(() => received is not null, "the typed payload must reach the handler");

        Check(received!.OrderId == "aot-a-1", "OrderId must round-trip");
        Check(received.Amount == 19.95m, "decimal Amount must round-trip");
        Check(received.Tags.Count == 2 && received.Tags[1] == "aot", "nested list values must round-trip");

        BOrderPlaced? receivedB = null;
        RabbitMqTestHooksB.OnInvoiceIssued = (evt, _) =>
        {
            receivedB = evt;
            return Task.CompletedTask;
        };

        var callsBefore = RabbitMqTestHooks.CallLog.Count;
        await PublishWithTopologyRetryAsync(bus, new CrestCreates.RabbitMq.TestContractsB.OrderPlacedEvent
        {
            OrderId = "aot-b-1",
            Total = 7m,
        });
        await WaitAsync(() => receivedB is not null, "the B assembly handler must receive its own event");

        Check(receivedB!.OrderId == "aot-b-1" && receivedB.Total == 7m, "B payload must round-trip");
        await Task.Delay(500);
        Check(
            RabbitMqTestHooks.CallLog.Count == callsBefore,
            "the same-short-name A handlers must not receive B's event");

        // Per-delivery scopes: publish a second A delivery and compare probe ids.
        await PublishWithTopologyRetryAsync(bus, new AOrderPlaced { OrderId = "aot-a-2", Amount = 1m });
        await WaitAsync(
            () => RabbitMqTestHooks.CallLog.Count(e => e.StartsWith("order:aot-a-2:")) == 1,
            "the second delivery must run");
        var probeIds = RabbitMqTestHooks.CallLog
            .Where(e => e.StartsWith("order:aot-a-"))
            .Select(e => e.Split(':')[2])
            .Distinct()
            .ToList();
        Check(probeIds.Count == 2, "each delivery must use its own DI scope");

        await host.StopAsync();
    }

    private static async Task RefusalsAsync()
    {
        using var host = BuildHost();
        await StartHostAsync(host);
        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();

        var handled = 0;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            handled++;
            return Task.CompletedTask;
        };

        await PublishWithTopologyRetryAsync(bus, new AOrderPlaced { OrderId = "aot-refuse-anchor", Amount = 1m });
        await WaitAsync(() => handled == 1, "anchor delivery must succeed");
        var baseline = RabbitMqTestHooks.CallLog.Count;

        var orderPlacedId = GeneratedEventDescriptor.GenerateId("orders.order-placed");
        await PublishRawAsync(QueueNames.Orders, "not-json-at-all");
        await PublishRawAsync(
            QueueNames.Orders,
            $$"""{"eventType":"orders.order-placed","eventId":"evt_unknown_127","eventVersion":1,"payload":"{}","headers":{},"timestamp":"2026-10-13T00:00:00Z"}""");
        await PublishRawAsync(
            QueueNames.Orders,
            $$"""{"eventType":"orders.order-placed","eventId":"{{orderPlacedId}}","payload":"{}","headers":{},"timestamp":"2026-10-13T00:00:00Z"}""");

        var dlqBaseline = await QueueDepthAsync(QueueNames.Orders + ".dlq");
        await WaitAsync(
            async () => await QueueDepthAsync(QueueNames.Orders + ".dlq") >= dlqBaseline + 2,
            "malformed and unknown messages must reach the dead-letter queue",
            seconds: 20);
        await Task.Delay(500);
        Check(
            RabbitMqTestHooks.CallLog.Count == baseline,
            "rejected messages must not trigger any business call");

        await host.StopAsync();
    }

    private static async Task LegacyAliasAsync()
    {
        using var host = BuildHost(builder => builder
            .AddLegacyWireAlias("AotLegacyOrderPlaced", "orders.order-placed", 1));
        await StartHostAsync(host);

        AOrderPlaced? received = null;
        RabbitMqTestHooks.OnOrderPlaced = (evt, _) =>
        {
            received = evt;
            return Task.CompletedTask;
        };

        await PublishWithTopologyRetryAsync(host.Services.GetRequiredService<RabbitMqEventBus>(),
            new AOrderPlaced { OrderId = "aot-legacy-anchor", Amount = 1m });
        await WaitAsync(() => received is not null, "anchor delivery must succeed");

        received = null;
        var dlqBaseline = await QueueDepthAsync(QueueNames.Orders + ".dlq");
        await PublishRawAsync(
            QueueNames.Orders,
            """{"eventType":"AotLegacyOrderPlaced","eventId":null,"eventVersion":null,"payload":"{\"orderId\":\"aot-legacy-1\",\"amount\":2.5,\"tags\":[],\"occurredOn\":\"2026-10-13T00:00:00Z\"}","headers":{},"timestamp":"2026-10-13T00:00:00Z"}""");

        await WaitAsync(() => received is not null, "the explicit alias must resolve the legacy identity");
        Check(received!.OrderId == "aot-legacy-1", "legacy payload must round-trip");
        await Task.Delay(500);
        Check(
            await QueueDepthAsync(QueueNames.Orders + ".dlq") == dlqBaseline,
            "the aliased message must not be dead-lettered");

        await host.StopAsync();
    }

    private static async Task RetryThenSuccessAsync()
    {
        using var host = BuildHost(options: o =>
        {
            o.RetryCount = 2;
            o.RetryDelaySeconds = 1;
        });
        await StartHostAsync(host);

        var attempts = 0;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("first native attempt fails");
            }

            return Task.CompletedTask;
        };

        await PublishWithTopologyRetryAsync(host.Services.GetRequiredService<RabbitMqEventBus>(),
            new AOrderPlaced { OrderId = "aot-retry-1", Amount = 3m });

        var dlqBaseline = await QueueDepthAsync(QueueNames.Orders + ".dlq");
        await WaitAsync(() => Volatile.Read(ref attempts) == 2, "the redelivered attempt must run", seconds: 20);
        await WaitAsync(
            async () => await QueueDepthAsync(QueueNames.Orders) == 0,
            "the successful retry must be acknowledged",
            seconds: 10);
        await Task.Delay(500);
        Check(
            await QueueDepthAsync(QueueNames.Orders + ".dlq") == dlqBaseline,
            "no dead letter after a successful retry");

        await host.StopAsync();
    }

    private static async Task ExhaustionAsync()
    {
        using var host = BuildHost(options: o =>
        {
            o.RetryCount = 0;
            o.RetryDelaySeconds = 0;
        });
        await StartHostAsync(host);

        var attempts = 0;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("native exhaustion");
        };

        var dlqBaseline = await QueueDepthAsync(QueueNames.Orders + ".dlq");
        await PublishWithTopologyRetryAsync(host.Services.GetRequiredService<RabbitMqEventBus>(),
            new AOrderPlaced { OrderId = "aot-exhaust-1", Amount = 1m });

        await WaitAsync(
            () => Volatile.Read(ref attempts) >= 1,
            "the failing delivery must run",
            seconds: 20);
        await WaitAsync(
            async () => await QueueDepthAsync(QueueNames.Orders + ".dlq") >= dlqBaseline + 1,
            "the exhausted message must reach the dead-letter queue",
            seconds: 20);
        await Task.Delay(500);
        Check(Volatile.Read(ref attempts) == 1, "RetryCount=0 must terminate after the first failure");

        await host.StopAsync();
    }

    private static async Task UnroutableAsync()
    {
        using var host = BuildHost(
            builder => builder
                .PublisherOnly()
                .AddPublishedEvent<AOrderPlaced>(routingKey: "aot127.nowhere"),
            options: o => o.Port = AmqpPort);
        await StartHostAsync(host);

        RabbitMqPublishException? publishException = null;
        try
        {
            await host.Services.GetRequiredService<RabbitMqEventBus>()
                .PublishAsync(new AOrderPlaced { OrderId = "aot-unroutable", Amount = 1m });
        }
        catch (RabbitMqPublishException ex)
        {
            publishException = ex;
        }

        Check(publishException is not null, "expected a typed RabbitMqPublishException for the unroutable publish");
        Check(
            publishException!.FailureKind == RabbitMqPublishFailureKind.Unroutable,
            $"expected Unroutable, got {publishException.FailureKind}");

        await host.StopAsync();
    }
}
