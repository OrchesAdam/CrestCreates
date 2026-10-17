using CrestCreates.Data.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 S1 baseline: locks the Ticket wire contract the current host actually publishes.
/// Every value here was read from the running host (EndpointDataSource + real HTTP), not from
/// test comments or generator documentation.
/// </summary>
public class TicketWireBaselineTests : BaseTest
{
    private static readonly (string Method, string Route, string Display, string Permission)[] FrozenMatrix =
    [
        ("POST", "api/ticket", "ITicketAppService.Create", "Ticket.Create"),
        ("GET", "api/ticket/{id}", "ITicketAppService.GetById", "Ticket.Get"),
        ("PUT", "api/ticket/{id}", "ITicketAppService.Update", "Ticket.Update"),
        ("DELETE", "api/ticket/{id}", "ITicketAppService.Delete", "Ticket.Delete"),
        ("GET", "api/ticket/all", "TicketApi.GetAll", "Ticket.GetAll"),
        ("GET", "api/ticket/assign", "ITicketAppService.Assign", "Ticket.Search"),
        ("GET", "api/ticket/resolve", "ITicketAppService.Resolve", "Ticket.Search"),
        ("GET", "api/ticket/close", "ITicketAppService.Close", "Ticket.Search"),
        ("GET", "api/ticket/by-customer/{customerId}", "ITicketAppService.GetByCustomer", "Ticket.Get"),
        ("GET", "api/ticket/by-assignee/{assigneeId}", "ITicketAppService.GetByAssignee", "Ticket.Get"),
        ("GET", "api/ticket/overdue", "ITicketAppService.GetOverdue", "Ticket.Search"),
    ];

    public TicketWireBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void Published_ticket_endpoint_matrix_is_exactly_the_frozen_projection()
    {
        var dataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var published = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("api/ticket", StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => (
                Method: string.Join("|", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new List<string>()),
                Route: endpoint.RoutePattern.RawText!,
                Display: endpoint.DisplayName ?? string.Empty,
                Permission: string.Join(",", endpoint.Metadata.GetMetadata<CrestCreates.DynamicApi.DynamicApiPermissionMetadata>()?.Permissions ?? Array.Empty<string>())))
            .OrderBy(row => row.Route)
            .ThenBy(row => row.Method)
            .ToList();

        published.Should().BeEquivalentTo(
            FrozenMatrix.Select(row => row).OrderBy(row => row.Route).ThenBy(row => row.Method),
            "the S1 baseline froze the eleven Ticket endpoints published by the legacy generated projection");
    }

    [Fact]
    public void Ticket_endpoints_publish_no_endpoint_level_authorization_metadata()
    {
        var dataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var ticketEndpoints = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("api/ticket", StringComparison.OrdinalIgnoreCase))
            .ToList();

        ticketEndpoints.Should().HaveCount(11);
        ticketEndpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>())
            .Should().BeEmpty(
                "permission enforcement today happens inside each generated handler through DynamicApiGeneratedRuntime.EnsurePermissionAsync, not through endpoint authorization metadata");
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_before_any_business_execution()
    {
        var client = CreateTenantClient(HostTenantId);

        var read = await client.GetAsync("/api/ticket/all");
        var write = await client.PostAsync("/api/ticket", new StringContent("{}", Encoding.UTF8, "application/json"));

        read.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        write.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await read.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be("Crest.Auth.Unauthorized");
    }

    [Fact]
    public async Task GetAll_is_published_by_the_hand_written_controller_override_with_its_own_permission_name()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await client.GetAsync("/api/ticket/all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("data", out var data).Should().BeTrue();
        data.ValueKind.Should().Be(JsonValueKind.Array);

        var dataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var getAll = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "api/ticket/all");
        getAll.DisplayName.Should().Be("TicketApi.GetAll");
        getAll.Metadata.GetMetadata<CrestCreates.DynamicApi.DynamicApiPermissionMetadata>()!.Permissions
            .Should().BeEquivalentTo(["Ticket.GetAll"]);
    }

    [Fact]
    public async Task Domain_rules_keep_the_recorded_status_semantics_on_the_write_path()
    {
        using var normalized = NormalizeDuplicateUnitOfWorkBinding(Factory);
        var (client, _) = await CreateAuthenticatedAdminClient(normalized);
        var ticketId = await CreateTicketThroughHttpAsync(client);

        var close = await client.GetAsync($"/api/ticket/close?id={ticketId}");
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        using var closedDocument = JsonDocument.Parse(await close.Content.ReadAsStringAsync());
        closedDocument.RootElement.GetProperty("data").GetProperty("status").GetInt32().Should().Be(6);

        var resolveAgain = await client.GetAsync($"/api/ticket/resolve?id={ticketId}");
        resolveAgain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var resolveBody = await resolveAgain.Content.ReadAsStringAsync();
        JsonDocument.Parse(resolveBody).RootElement.GetProperty("code").GetString().Should().Be("Crest.Operation.Invalid");
    }

    [Fact]
    public async Task Assign_without_an_active_sla_policy_leaves_due_date_null()
    {
        using var normalized = NormalizeDuplicateUnitOfWorkBinding(Factory);
        var (client, _) = await CreateAuthenticatedAdminClient(normalized);
        var ticketId = await CreateTicketThroughHttpAsync(client);

        var response = await client.GetAsync($"/api/ticket/assign?id={ticketId}&agentId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("status").GetInt32().Should().Be(2);
        data.GetProperty("assigneeId").ValueKind.Should().Be(JsonValueKind.String);
        data.GetProperty("dueDate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<Guid> CreateTicketThroughHttpAsync(HttpClient client)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        var customerId = await HelpdeskTestData.CreateCustomerAsync(scopeFactory);
        var payload = $"{{\"title\":\"baseline ticket title\",\"description\":\"baseline ticket description\",\"priority\":2,\"type\":2,\"customerId\":\"{customerId}\"}}";
        var response = await client.PostAsync("/api/ticket", new StringContent(payload, Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Guid.Parse(document.RootElement.GetProperty("data").GetProperty("id").GetString()!);
    }

    private static WebApplicationFactory<global::Program> NormalizeDuplicateUnitOfWorkBinding(
        Fixtures.HelpdeskWebApplicationFactory factory)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Measurement normalisation for finding R-01 (three EfCore unit-of-work bindings):
            // without it every write action fails while building the provider registry, so the
            // wire behaviour of the write path could not be observed at all. R-01 itself is locked
            // by TicketProjectionGapBaselineTests.
            foreach (var duplicate in services.Where(d => d.ServiceType == typeof(UnitOfWorkProviderBinding)).Skip(1).ToList())
            {
                services.Remove(duplicate);
            }
        }));

    private static async Task<(HttpClient Client, TokenResponse Token)> CreateAuthenticatedAdminClient(
        WebApplicationFactory<global::Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", HostTenantId);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = AdminUserName,
            ["password"] = AdminPassword,
            ["client_id"] = "test-client",
            ["scope"] = "openid profile offline_access",
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", document.RootElement.GetProperty("access_token").GetString());
        return (client, new TokenResponse());
    }
}

internal static class HelpdeskTestData
{
    internal static async Task<Guid> CreateCustomerAsync(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SaaSHelpdesk.EntityFrameworkCore.HelpdeskDbContext>();
        var customer = new SaaSHelpdesk.Domain.Entities.Customer(
            Guid.NewGuid(),
            $"Base Customer {Guid.NewGuid():N}"[..20],
            $"base_{Guid.NewGuid():N}@test.com",
            Guid.NewGuid());
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();
        return customer.Id;
    }
}
