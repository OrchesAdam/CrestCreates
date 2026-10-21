using System.Net;
using System.Text;
using System.Text.Json;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Native Ticket scenario matrix. The published binary binds an ephemeral loopback port, this method
/// drives the real generated HTTP surface and real TicketAppService over HttpClient, prints one
/// sentinel per scenario, and prints the total sentinel only when every scenario passed.
/// </summary>
public static class TicketNativeGate
{
    public const string TotalSentinel = "CRESTCREATES_TICKET_NATIVE_OK";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static bool _failed;

    public static async Task RunAsync(WebApplication app, string[] args)
    {
        var mutation = args
            .FirstOrDefault(argument => argument.StartsWith("--ticket-mutation=", StringComparison.Ordinal))?
            .Split('=', 2)[1];

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .First();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(address),
            Timeout = RequestTimeout,
        };

        await RunScenariosAsync(app, client, mutation);

        await app.StopAsync();
        if (!_failed)
        {
            Console.WriteLine(TotalSentinel);
            Environment.ExitCode = 0;
        }
        else
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task RunScenariosAsync(
        WebApplication app,
        HttpClient client,
        string? mutation)
    {
        var store = app.Services.GetRequiredService<TicketMemoryStore>();
        var permissionChecker = (FixturePermissionChecker)app.Services.GetRequiredService<IPermissionChecker>();
        var customerId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        Guid ticketId = Guid.Empty;
        string stamp = string.Empty;

        await Scenario("create", async () =>
        {
            var response = await SendJsonAsync(client, HttpMethod.Post, "/api/ticket",
                CreateBody("Native ticket", "Native ticket description", customerId));
            Check(response.StatusCode == HttpStatusCode.OK, $"create status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            ticketId = data.GetProperty("id").GetGuid();
            stamp = data.GetProperty("concurrencyStamp").GetString()!;
            var expectedTitle = mutation == "wrong-expectation" ? "Mutated title" : "Native ticket";
            Check(data.GetProperty("title").GetString() == expectedTitle, "create payload title mismatch");
            Check(!string.IsNullOrWhiteSpace(stamp), "create must return a stamp");
            Check(store.TicketCount == 1, "create must persist exactly one row");
        });

        await Scenario("get-by-id", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/{ticketId}");
            Check(response.StatusCode == HttpStatusCode.OK, $"get status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check(document.RootElement.GetProperty("data").GetProperty("id").GetGuid() == ticketId, "get id mismatch");
        });

        await Scenario("get-unknown-answers-404", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/{Guid.NewGuid()}");
            Check(response.StatusCode == HttpStatusCode.NotFound, $"unknown get status {(int)response.StatusCode}");
        });

        await Scenario("list-all", async () =>
        {
            var response = await client.GetAsync("/api/ticket/all");
            Check(response.StatusCode == HttpStatusCode.OK, $"all status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check(ContainsId(document.RootElement.GetProperty("data"), ticketId), "all must contain the created ticket");
        });

        await Scenario("list-by-customer", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/by-customer/{customerId}");
            Check(response.StatusCode == HttpStatusCode.OK, $"by-customer status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check(ContainsId(document.RootElement.GetProperty("data"), ticketId), "by-customer must contain the ticket");
        });

        await Scenario("update-with-stamp", async () =>
        {
            var response = await SendJsonAsync(client, HttpMethod.Put, $"/api/ticket/{ticketId}",
                UpdateBody("Updated native ticket", "Updated native ticket description", customerId, stamp));
            Check(response.StatusCode == HttpStatusCode.OK, $"update status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Check(data.GetProperty("title").GetString() == "Updated native ticket", "update title mismatch");
            var nextStamp = data.GetProperty("concurrencyStamp").GetString()!;
            Check(nextStamp != stamp, "update must rotate the stamp");
            stamp = nextStamp;
        });

        await Scenario("assign-with-active-sla", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/assign?id={ticketId}&agentId={agentId}");
            Check(response.StatusCode == HttpStatusCode.OK, $"assign status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Check(data.GetProperty("assigneeId").GetGuid() == agentId, "assign must set the agent");
            Check(data.GetProperty("dueDate").ValueKind != JsonValueKind.Null, "active SLA must set a due date");
            stamp = data.GetProperty("concurrencyStamp").GetString()!;
        });

        await Scenario("list-by-assignee", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/by-assignee/{agentId}");
            Check(response.StatusCode == HttpStatusCode.OK, $"by-assignee status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check(ContainsId(document.RootElement.GetProperty("data"), ticketId), "by-assignee must contain the ticket");
        });

        await Scenario("resolve", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/resolve?id={ticketId}");
            Check(response.StatusCode == HttpStatusCode.OK, $"resolve status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Check(data.GetProperty("status").GetInt32() == 5, "resolve must move the ticket to Resolved");
            Check(data.GetProperty("resolvedAt").ValueKind != JsonValueKind.Null, "resolve must set resolvedAt");
            stamp = data.GetProperty("concurrencyStamp").GetString()!;
        });

        await Scenario("close", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/close?id={ticketId}");
            Check(response.StatusCode == HttpStatusCode.OK, $"close status {(int)response.StatusCode}");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Check(data.GetProperty("status").GetInt32() == 6, "close must move the ticket to Closed");
            stamp = data.GetProperty("concurrencyStamp").GetString()!;
        });

        await Scenario("business-failure-is-serializable", async () =>
        {
            var response = await client.GetAsync($"/api/ticket/resolve?id={ticketId}");
            Check(response.StatusCode != HttpStatusCode.OK, "resolving a closed ticket must fail");
            var body = await response.Content.ReadAsStringAsync();
            Check(!string.IsNullOrWhiteSpace(body), "the business failure body must not be empty");
            using var _ = JsonDocument.Parse(body);
        });

        await Scenario("delete-with-if-match", async () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/ticket/{ticketId}");
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp}\"");
            var response = await client.SendAsync(request);
            Check(response.StatusCode == HttpStatusCode.OK, $"delete status {(int)response.StatusCode}");
            var after = await client.GetAsync($"/api/ticket/{ticketId}");
            Check(after.StatusCode == HttpStatusCode.NotFound, "deleted ticket must answer 404");
        });

        await Scenario("invalid-body-zero-business", async () =>
        {
            var before = store.TicketCount;
            var response = await SendJsonAsync(client, HttpMethod.Post, "/api/ticket",
                CreateBody("x", "short", customerId));
            var expected = mutation == "wrong-validation-status" ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            Check(response.StatusCode == expected, $"invalid body status {(int)response.StatusCode}");
            Check(store.TicketCount == before, "invalid input must not reach the business service");
        });

        await Scenario("permission-denied", async () =>
        {
            var created = await SendJsonAsync(client, HttpMethod.Post, "/api/ticket",
                CreateBody("Denied ticket", "Denied ticket description", customerId));
            Check(created.StatusCode == HttpStatusCode.OK, "denied control create failed");
            using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var deniedId = createdDocument.RootElement.GetProperty("data").GetProperty("id").GetGuid();

            permissionChecker.Denied.Add("Ticket.Update");
            try
            {
                var denial = await client.GetAsync($"/api/ticket/assign?id={deniedId}&agentId={agentId}");
                Check(denial.StatusCode == HttpStatusCode.Forbidden,
                    $"permission denial status {(int)denial.StatusCode}");
            }
            finally
            {
                permissionChecker.Denied.Remove("Ticket.Update");
            }
        });

        await Scenario("metadata-rejection", async () =>
        {
            var probeBuilder = WebApplication.CreateBuilder();
            probeBuilder.Services.AddTicketFixture();
            probeBuilder.Services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
            probeBuilder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(
                options => options.SerializerOptions.TypeInfoResolverChain.Clear());

            await using var probe = probeBuilder.Build();
            var endpoints = new ProbeEndpointRouteBuilder(probe.Services);
            try
            {
                endpoints.MapCrestCapabilityEndpoints();
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains("JSON contract root", StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException("the map accepted a host whose JSON resolver chain cannot answer the Ticket roots");
        });
    }

    private sealed class ProbeEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() => throw new NotSupportedException();
    }

    private static async Task Scenario(string name, Func<Task> body)
    {
        try
        {
            await body();
            Console.WriteLine($"{name}:PASS");
        }
        catch (Exception exception)
        {
            _failed = true;
            Console.WriteLine($"{name}:FAIL: {exception.Message}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool ContainsId(JsonElement array, Guid id)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.GetProperty("id").GetGuid() == id)
            {
                return true;
            }
        }

        return false;
    }

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, string json)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return client.SendAsync(request);
    }

    private static string CreateBody(string title, string description, Guid customerId)
        => $$"""
            {"title":"{{title}}","description":"{{description}}","priority":2,"type":2,"customerId":"{{customerId}}"}
            """;

    private static string UpdateBody(string title, string description, Guid customerId, string stamp)
        => $$"""
            {"id":"{{Guid.Empty}}","title":"{{title}}","description":"{{description}}","status":1,"priority":2,"type":2,"customerId":"{{customerId}}","assigneeId":null,"categoryId":null,"dueDate":null,"resolvedAt":null,"closedAt":null,"isEscalated":false,"concurrencyStamp":"{{stamp}}"}
            """;
}
