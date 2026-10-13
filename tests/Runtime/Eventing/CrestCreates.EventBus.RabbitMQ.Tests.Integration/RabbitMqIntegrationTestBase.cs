using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.RabbitMq.TestContractsA;
using CrestCreates.RabbitMq.TestContractsB;
using CrestCreates.RabbitMq.TestHandlersA;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

/// <summary>
/// Integration harness for the official static-binding mainline: a real broker (podman-managed
/// rabbitmq:4.0-management-alpine), a dedicated management API, an isolated vhost per test and hosts
/// built exclusively through AddRabbitMqEventBus plus the generated activation extensions. There is no
/// hand-configured consumer anywhere in these tests, and a missing broker fails the suite explicitly
/// instead of silently skipping.
/// </summary>
public abstract class RabbitMqIntegrationTestBase : IAsyncLifetime
{
    protected ITestOutputHelper Output { get; }

    protected RabbitMqIntegrationTestBase(ITestOutputHelper output) => Output = output;

    private static readonly string HostName = Environment.GetEnvironmentVariable("CREST_RABBITMQ_HOST") ?? "localhost";
    private static readonly int AmqpPort = int.TryParse(Environment.GetEnvironmentVariable("CREST_RABBITMQ_PORT"), out var port) ? port : 5672;
    private static readonly int ManagementPort = int.TryParse(Environment.GetEnvironmentVariable("CREST_RABBITMQ_MGMT_PORT"), out var mgmtPort) ? mgmtPort : 15672;
    private static readonly HttpClient Management = CreateManagementClient();

    private readonly List<IHost> _hosts = new();
    private readonly List<IAsyncDisposable> _disposables = new();

    protected string VHost { get; private set; } = string.Empty;

    protected RabbitMqOptions DefaultOptions(Action<RabbitMqOptions>? configure = null)
    {
        var options = new RabbitMqOptions
        {
            HostName = HostName,
            Port = AmqpPort,
            UserName = "guest",
            Password = "guest",
            VirtualHost = VHost,
            MaxChannels = 5,
            RetryCount = 2,
            RetryDelaySeconds = 1,
            DeadLetterExchange = "itest127.dlx",
            DefaultExchange = "itest127.events",
            PublisherConfirmTimeoutSeconds = 15,
        };
        configure?.Invoke(options);
        return options;
    }

    public virtual async Task InitializeAsync()
    {
        await EnsureBrokerAsync();
        VHost = "itest127_" + Guid.NewGuid().ToString("N")[..12];
        await CreateVHostAsync(VHost);
        RabbitMqTestHooks.Reset();
        RabbitMqTestHooksB.Reset();
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            try
            {
                await host.StopAsync(TimeSpan.FromSeconds(20));
            }
            catch (Exception)
            {
            }

            host.Dispose();
        }

        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }

        if (!string.IsNullOrEmpty(VHost))
        {
            await DeleteVHostAsync(VHost);
        }
    }

    /// <summary>
    /// Builds and starts a host through the single AddRabbitMqEventBus entry with the generated
    /// contract/handler activations explicitly applied.
    /// </summary>
    protected async Task<IHost> StartHostAsync(
        Action<RabbitMqEventBusBuilder>? builder = null,
        Action<RabbitMqOptions>? options = null,
        bool contractA = true,
        bool contractB = true,
        bool handlersA = true,
        bool handlersB = true,
        IReadOnlyList<string>? waitForQueues = null)
    {
        var host = BuildHost(builder, options, contractA, contractB, handlersA, handlersB);
        await host.StartAsync();
        _hosts.Add(host);

        // BackgroundService.StartAsync returns before the consumer declares topology, so tests wait
        // for the expected queues to exist before publishing anything.
        var expected = waitForQueues ?? DefaultExpectedQueues(handlersA, handlersB);
        foreach (var queue in expected)
        {
            var declared = await WaitUntilAsync(
                async () => await QueueExistsAsync(VHost, queue),
                TimeSpan.FromSeconds(15));
            declared.Should().BeTrue($"queue '{queue}' must be declared by the running consumer");
        }

        return host;
    }

    private static List<string> DefaultExpectedQueues(bool handlersA, bool handlersB)
    {
        var queues = new List<string>();
        if (handlersA)
        {
            queues.Add(CrestCreates.RabbitMq.TestHandlersA.QueueNames.Orders);
            queues.Add(CrestCreates.RabbitMq.TestHandlersA.QueueNames.Inventory);
        }

        if (handlersB)
        {
            queues.Add(CrestCreates.RabbitMq.TestContractsB.InvoiceIssuedHandler.Queue);
        }

        return queues;
    }

    protected static async Task<bool> QueueExistsAsync(string vhost, string queue)
    {
        try
        {
            await GetQueueAsync(vhost, queue);
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    protected IHost BuildHost(
        Action<RabbitMqEventBusBuilder>? builder = null,
        Action<RabbitMqOptions>? options = null,
        bool contractA = true,
        bool contractB = true,
        bool handlersA = true,
        bool handlersB = true)
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Logging.SetMinimumLevel(LogLevel.Information);
        hostBuilder.Logging.AddProvider(new XunitLoggerProvider(Output));
        var services = hostBuilder.Services;

        services.AddScoped<RabbitMqScopedProbe>();

        if (contractA)
        {
            CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsA.EventRegistrations
                .AddEventDescriptors(services);
        }

        if (contractB)
        {
            CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsB.EventRegistrations
                .AddEventDescriptors(services);
        }

        if (handlersA)
        {
            CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestHandlersA.RabbitMqSubscriptions
                .AddRabbitMqSubscriptions(services);
        }

        if (handlersB)
        {
            CrestCreates.Generated.CrestCreates_EventBus_RabbitMQ_TestContractsB.RabbitMqSubscriptions
                .AddRabbitMqSubscriptions(services);
        }

        var effectiveOptions = DefaultOptions(options);
        services.AddRabbitMqEventBus(configure: o =>
        {
            o.HostName = effectiveOptions.HostName;
            o.Port = effectiveOptions.Port;
            o.UserName = effectiveOptions.UserName;
            o.Password = effectiveOptions.Password;
            o.VirtualHost = effectiveOptions.VirtualHost;
            o.MaxChannels = effectiveOptions.MaxChannels;
            o.RetryCount = effectiveOptions.RetryCount;
            o.RetryDelaySeconds = effectiveOptions.RetryDelaySeconds;
            o.DeadLetterExchange = effectiveOptions.DeadLetterExchange;
            o.DefaultExchange = effectiveOptions.DefaultExchange;
            o.PublisherConfirmTimeoutSeconds = effectiveOptions.PublisherConfirmTimeoutSeconds;
            o.DefaultPrefetchCount = effectiveOptions.DefaultPrefetchCount;
            o.ConsumerDispatchConcurrency = effectiveOptions.ConsumerDispatchConcurrency;
        }, builder: b =>
        {
            builder?.Invoke(b);

            if (handlersB)
            {
                // ContractsB keeps a deployed-binding routing key on both sides: the subscription
                // declares an explicit RoutingKey and the publish binding mirrors it (migration
                // semantics — neither side silently renames the other).
                b.AddPublishedEvent<CrestCreates.RabbitMq.TestContractsB.OrderPlacedEvent>(
                    routingKey: CrestCreates.RabbitMq.TestContractsB.InvoiceIssuedHandler.RoutingKey);
            }
        });

        return hostBuilder.Build();
    }

    // ── Broker probes ──

    protected static Task<JsonElement> GetQueueAsync(string vhost, string queue)
        => ManagementJsonAsync(HttpMethod.Get, $"queues/{vhost}/{queue}");

    protected static async Task<long> GetQueueDepthAsync(string vhost, string queue, string field)
    {
        try
        {
            var json = await GetQueueAsync(vhost, queue);
            return json.TryGetProperty(field, out var value) ? value.GetInt64() : 0;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return 0;
        }
    }

    /// <summary>Reads one message (ackmode=ack_requeue_false) from a queue via the management API.</summary>
    protected static async Task<ManagementMessage?> GetMessageAsync(string vhost, string queue)
    {
        var response = await ManagementJsonAsync(
            HttpMethod.Post,
            $"queues/{vhost}/{queue}/get",
            """{"count":1,"ackmode":"ack_requeue_false","encoding":"auto"}""");

        if (!response.EnumerateArray().Any())
        {
            return null;
        }

        var entry = response.EnumerateArray().First();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (entry.TryGetProperty("properties", out var properties) &&
            properties.TryGetProperty("headers", out var headersElement) &&
            headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var header in headersElement.EnumerateObject())
            {
                headers[header.Name] = header.Value.ValueKind switch
                {
                    JsonValueKind.String => header.Value.GetString(),
                    JsonValueKind.Number => header.Value.GetRawText(),
                    _ => header.Value.GetRawText(),
                };
            }
        }

        return new ManagementMessage(
            entry.GetProperty("payload").GetString() ?? string.Empty,
            properties.TryGetProperty("message_id", out var messageId) ? messageId.GetString() : null,
            properties.TryGetProperty("correlation_id", out var correlationId) ? correlationId.GetString() : null,
            headers);
    }

    /// <summary>Publishes a raw body through the default exchange (test-harness side, never a framework path).</summary>
    protected static async Task PublishRawAsync(
        string vhost,
        string queue,
        string payload,
        Dictionary<string, object?>? headers = null,
        string? messageId = null,
        string? correlationId = null)
    {
        var factory = new global::RabbitMQ.Client.ConnectionFactory
        {
            HostName = HostName,
            Port = AmqpPort,
            UserName = "guest",
            Password = "guest",
            VirtualHost = vhost,
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var body = Encoding.UTF8.GetBytes(payload);
        var basicProperties = new global::RabbitMQ.Client.BasicProperties
        {
            MessageId = messageId,
            CorrelationId = correlationId,
        };

        if (headers is not null)
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var header in headers)
            {
                dictionary[header.Key] = header.Value;
            }

            basicProperties.Headers = dictionary;
        }

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: true,
            basicProperties: basicProperties,
            body: body);
    }

    protected static async Task CloseAllConnectionsAsync(string vhost)
    {
        var connections = await ManagementJsonAsync(HttpMethod.Get, "connections");
        foreach (var connection in connections.EnumerateArray())
        {
            if (connection.TryGetProperty("vhost", out var connectionVhost) &&
                connectionVhost.GetString() == vhost &&
                connection.TryGetProperty("name", out var name))
            {
                var encodedName = Uri.EscapeDataString(name.GetString()!);
                using var response = await Management.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, $"connections/{encodedName}"));
            }
        }
    }

    protected static async Task<IReadOnlyList<string>> GetQueueNamesAsync(string vhost)
    {
        var queues = await ManagementJsonAsync(HttpMethod.Get, $"queues/{vhost}");
        return queues.EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString()!)
            .ToList();
    }

    protected static async Task<bool> WaitUntilAsync(Func<Task<bool>> probe, TimeSpan timeout, string? because = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await probe())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return await probe();
    }

    // ── Management API plumbing ──

    private static HttpClient CreateManagementClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{HostName}:{ManagementPort}/api/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes("guest:guest")));
        return client;
    }

    private static async Task EnsureBrokerAsync()
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(HostName, AmqpPort, cts.Token);
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"RabbitMQ is not reachable at {HostName}:{AmqpPort} ({ex.Message}). " +
                "Start it with: DOCKER_HOST=unix:///run/user/1000/podman/podman.sock podman start crestcreates-test-rabbitmq");
        }

        try
        {
            using var response = await Management.GetAsync("overview");
            if (!response.IsSuccessStatusCode)
            {
                Assert.Fail($"RabbitMQ management API returned {(int)response.StatusCode} at http://{HostName}:{ManagementPort}/api/.");
            }
        }
        catch (Exception ex)
        {
            Assert.Fail($"RabbitMQ management API is not reachable at http://{HostName}:{ManagementPort}/api/ ({ex.Message}).");
        }
    }

    private static async Task CreateVHostAsync(string vhost)
    {
        using var response = await Management.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"vhosts/{vhost}")
        {
            Content = new StringContent("""{"description":"itest127","tags":[]}""", Encoding.UTF8, "application/json"),
        });

        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.NoContent))
        {
            Assert.Fail($"Failed to create test vhost '{vhost}': {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        // Grant the guest user full permissions on the isolated vhost.
        using var permissions = await Management.SendAsync(new HttpRequestMessage(
            HttpMethod.Put, $"permissions/{vhost}/guest")
        {
            Content = new StringContent("""{"configure":".*","write":".*","read":".*"}""", Encoding.UTF8, "application/json"),
        });

        if (permissions.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.NoContent))
        {
            Assert.Fail($"Failed to grant permissions on vhost '{vhost}': {(int)permissions.StatusCode}");
        }
    }

    private static async Task DeleteVHostAsync(string vhost)
    {
        try
        {
            using var response = await Management.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"vhosts/{vhost}"));
        }
        catch (Exception)
        {
        }
    }

    protected static async Task<JsonElement> ManagementJsonAsync(HttpMethod method, string path, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await Management.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Management API {method} {path} returned {(int)response.StatusCode}: {content}",
                inner: null,
                response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }
}

public sealed record ManagementMessage(
    string Payload,
    string? MessageId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string?> Headers);
