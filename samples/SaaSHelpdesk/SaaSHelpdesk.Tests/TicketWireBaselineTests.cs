using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 S3: locks the formal Ticket wire contract published by the generated ExplicitHost
/// contribution. The S1 legacy snapshot is preserved in the design/evidence docs; this test now
/// asserts the target matrix (including GetAll owned by the formal contribution with Ticket.Search).
/// </summary>
public class TicketWireBaselineTests : BaseTest
{
    private static readonly (string Method, string Route, string CapabilityId, string Permission)[] FrozenMatrix =
    [
        ("POST", "/api/ticket", "compat.appservice.ticket.create", "Ticket.Create"),
        ("GET", "/api/ticket/{id}", "compat.appservice.ticket.get-by-id", "Ticket.Get"),
        ("PUT", "/api/ticket/{id}", "compat.appservice.ticket.update", "Ticket.Update"),
        ("DELETE", "/api/ticket/{id}", "compat.appservice.ticket.delete", "Ticket.Delete"),
        ("GET", "/api/ticket/all", "compat.appservice.ticket.get-all", "Ticket.Search"),
        ("GET", "/api/ticket/assign", "compat.appservice.ticket.assign", "Ticket.Update"),
        ("GET", "/api/ticket/resolve", "compat.appservice.ticket.resolve", "Ticket.Update"),
        ("GET", "/api/ticket/close", "compat.appservice.ticket.close", "Ticket.Update"),
        ("GET", "/api/ticket/by-customer/{customerId}", "compat.appservice.ticket.get-by-customer", "Ticket.Get"),
        ("GET", "/api/ticket/by-assignee/{assigneeId}", "compat.appservice.ticket.get-by-assignee", "Ticket.Get"),
        ("GET", "/api/ticket/overdue", "compat.appservice.ticket.get-overdue", "Ticket.Search"),
    ];

    public TicketWireBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void Published_ticket_endpoint_matrix_is_exactly_the_formal_projection()
    {
        var dataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var published = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/ticket", StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => (
                Method: string.Join("|", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new List<string>()),
                Route: endpoint.RoutePattern.RawText!))
            .OrderBy(row => row.Route)
            .ThenBy(row => row.Method)
            .ToList();

        published.Should().BeEquivalentTo(
            FrozenMatrix.Select(row => (row.Method, row.Route)).OrderBy(row => row.Route).ThenBy(row => row.Method),
            "the formal Ticket contribution owns exactly the eleven target endpoints");
        published.Should().HaveCount(11);

        var endpointRegistry = Factory.Services.GetRequiredService<CrestCreates.DynamicApi.ICapabilityEndpointRegistry>();
        var capabilityRegistry = Factory.Services.GetRequiredService<CrestCreates.Metadata.DescriptorCapability.ICapabilityRegistry>();
        var descriptors = endpointRegistry.GetAll()
            .Where(descriptor => descriptor.RoutePattern.StartsWith("/api/ticket", StringComparison.OrdinalIgnoreCase))
            .Select(descriptor => (
                Method: descriptor.HttpMethod.ToString().ToUpperInvariant(),
                Route: descriptor.RoutePattern,
                CapabilityId: descriptor.Capability.Id,
                Permission: capabilityRegistry.GetByVersion(descriptor.Capability.Id, descriptor.Capability.Version)!.Permissions.Single()))
            .OrderBy(row => row.Route)
            .ThenBy(row => row.Method)
            .ToList();
        descriptors.Should().BeEquivalentTo(
            FrozenMatrix.OrderBy(row => row.Route).ThenBy(row => row.Method),
            "the capability descriptors carry the frozen five-permission matrix");
    }

    [Fact]
    public void Ticket_endpoints_publish_no_endpoint_level_authorization_metadata()
    {
        var dataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var ticketEndpoints = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/ticket", StringComparison.OrdinalIgnoreCase))
            .ToList();

        ticketEndpoints.Should().HaveCount(11);
        ticketEndpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>())
            .Should().BeEmpty(
                "permission enforcement is performed by the capability pipeline's authorization middleware; " +
                "the generated contribution declares InheritCapability and must not bypass it with endpoint metadata");
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
    public async Task GetAll_is_owned_by_the_formal_contribution_with_ticket_search()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await client.GetAsync("/api/ticket/all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("data", out var data).Should().BeTrue();
        data.ValueKind.Should().Be(JsonValueKind.Array);

        var getAllDescriptor = Factory.Services
            .GetRequiredService<CrestCreates.DynamicApi.ICapabilityEndpointRegistry>()
            .GetAll()
            .Single(descriptor => descriptor.RoutePattern == "/api/ticket/all");
        getAllDescriptor.Capability.Id.Should().Be("compat.appservice.ticket.get-all");
        Factory.Services.GetRequiredService<CrestCreates.Metadata.DescriptorCapability.ICapabilityRegistry>()
            .GetByVersion(getAllDescriptor.Capability.Id, getAllDescriptor.Capability.Version)!
            .Permissions.Should().BeEquivalentTo(["Ticket.Search"]);
    }

    [Fact]
    public async Task Domain_rules_keep_the_recorded_status_semantics_on_the_write_path()
    {
        var (client, _) = await CreateAuthenticatedAdminClient(Factory);
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
        var (client, _) = await CreateAuthenticatedAdminClient(Factory);
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
