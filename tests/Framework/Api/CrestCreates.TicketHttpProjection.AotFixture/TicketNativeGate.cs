using System.Net;
using System.Text;
using System.Text.Json;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Mvc;
using SaaSHelpdesk.Application.Contracts.DTOs;
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
        var observer = app.Services.GetRequiredService<TicketBusinessObserver>();
        var permissionChecker = (FixturePermissionChecker)app.Services.GetRequiredService<IPermissionChecker>();
        var customerId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        Guid ticketId = Guid.Empty;
        string stamp = string.Empty;

        await Scenario("create", async () =>
        {
            var before = observer.Capture();
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
            Check(observer.BusinessEntries == before.BusinessEntries + 1,
                "the real business service must be entered exactly once for create");
            Check(observer.UnitOfWorkBegins == before.UnitOfWorkBegins + 1,
                "create must start exactly one unit of work");
            Check(observer.RepositoryWrites == before.RepositoryWrites + 1,
                "create must perform exactly one repository write");
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
            var beforeRows = store.TicketCount;
            var beforeCounters = observer.Capture();
            var response = await SendJsonAsync(client, HttpMethod.Post, "/api/ticket",
                CreateBody("x", "short", customerId));
            var expected = mutation == "wrong-validation-status" ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            Check(response.StatusCode == expected, $"invalid body status {(int)response.StatusCode}");
            Check(store.TicketCount == beforeRows, "invalid input must not persist a row");
            Check(observer.BusinessEntries == beforeCounters.BusinessEntries,
                "invalid input must not enter the real business service");
            Check(observer.UnitOfWorkBegins == beforeCounters.UnitOfWorkBegins,
                "invalid input must not start a unit of work");
            Check(observer.RepositoryWrites == beforeCounters.RepositoryWrites,
                "invalid input must not reach the repository write path");
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
            const string owner = "CrestCreates.TicketHttpProjection.AotFixture";
            var probeBuilder = WebApplication.CreateBuilder();
            probeBuilder.Services.AddTicketFixture();
            if (mutation == "decoy-metadata")
            {
                probeBuilder.Services.RemoveAll<CapabilityEndpointContribution>();
                probeBuilder.Services.AddCrestCapabilityEndpointContribution(new ReviewDecoyContribution());
            }

            probeBuilder.Services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
            probeBuilder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(
                options => options.SerializerOptions.TypeInfoResolverChain.Clear());

            await using var probe = probeBuilder.Build();

            // Lock the negative probe to the real Ticket owner, all 11 actions/identities and target
            // JSON roots before breaking the resolver chain. A decoy contribution with no Ticket
            // endpoints must fail this precondition, not pass as "some JSON error happened".
            var contributions = probe.Services.GetServices<CapabilityEndpointContribution>().ToArray();
            var ticket = contributions.SingleOrDefault(contribution => contribution.OwnerAssemblyName == owner);
            Check(ticket is not null, "the metadata probe must contain the Ticket contribution");
            Check(
                ticket!.EndpointDescriptors.Select(endpoint => endpoint.Id).OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(ExpectedEndpointIds.OrderBy(id => id, StringComparer.Ordinal)),
                "the metadata probe must contain all 11 Ticket endpoint identities");
            Check(
                ticket.CapabilityDescriptors.Select(capability => capability.Id).OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(ExpectedCapabilityIds.OrderBy(id => id, StringComparer.Ordinal)),
                "the metadata probe must contain all 11 Ticket capability identities");
            Check(ticket.RequiredIdentityIds.Count == ExpectedEndpointIds.Length,
                "the Ticket contribution must require all 11 endpoint identities");
            Check(ticket.JsonContext is not null, "the Ticket contribution must carry its generated JSON context");
            Check(ticket.RequiredJsonRootTypes.Any(root =>
                    root.ClrType == typeof(CreateTicketDto)
                    && root.Action == "CreateAsync"
                    && root.Component == "Body"),
                "the Ticket contribution must require the CreateTicketDto body root");
            Check(ticket.RequiredJsonRootTypes.Any(root =>
                    root.ClrType == typeof(DynamicApiResponse<TicketDto>)
                    && root.Action == "CreateAsync"
                    && root.Component == "ResultEnvelope"),
                "the Ticket contribution must require the Ticket result envelope root");

            var endpoints = new ProbeEndpointRouteBuilder(probe.Services);
            try
            {
                endpoints.MapCrestCapabilityEndpoints();
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains($"owner '{owner}'", StringComparison.Ordinal)
                      && exception.Message.Contains("action 'CreateAsync'", StringComparison.Ordinal)
                      && exception.Message.Contains(
                          "type 'SaaSHelpdesk.Application.Contracts.DTOs.CreateTicketDto'",
                          StringComparison.Ordinal))
            {
                Check(
                    endpoints.DataSources.SelectMany(source => source.Endpoints).Count() == 0,
                    "a failed metadata map must not leave any route behind");
                return;
            }

            throw new InvalidOperationException(
                "the map did not reject with the Ticket owner/action/body-root attribution");
        });
    }

    private static readonly string[] ExpectedEndpointIds =
    [
        "endpoint:compat.appservice.ticket.create",
        "endpoint:compat.appservice.ticket.get-by-id",
        "endpoint:compat.appservice.ticket.get-all",
        "endpoint:compat.appservice.ticket.update",
        "endpoint:compat.appservice.ticket.delete",
        "endpoint:compat.appservice.ticket.assign",
        "endpoint:compat.appservice.ticket.resolve",
        "endpoint:compat.appservice.ticket.close",
        "endpoint:compat.appservice.ticket.get-by-customer",
        "endpoint:compat.appservice.ticket.get-by-assignee",
        "endpoint:compat.appservice.ticket.get-overdue",
    ];

    private static readonly string[] ExpectedCapabilityIds =
    [
        "compat.appservice.ticket.create",
        "compat.appservice.ticket.get-by-id",
        "compat.appservice.ticket.get-all",
        "compat.appservice.ticket.update",
        "compat.appservice.ticket.delete",
        "compat.appservice.ticket.assign",
        "compat.appservice.ticket.resolve",
        "compat.appservice.ticket.close",
        "compat.appservice.ticket.get-by-customer",
        "compat.appservice.ticket.get-by-assignee",
        "compat.appservice.ticket.get-overdue",
    ];

    private sealed class ReviewDecoyContribution : CapabilityEndpointContribution
    {
        public ReviewDecoyContribution()
        {
            OwnerAssemblyName = "review-decoy";
            JsonContext = TicketFailureJsonContext.Default;
            RequiredJsonRootTypes = new[]
            {
                new CapabilityEndpointJsonRootType(typeof(ProblemDetails), "Decoy", "Payload"),
            };
        }
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
