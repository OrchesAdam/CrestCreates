using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 S1 red baseline. Each test fails for exactly one capability gap recorded in
/// docs/superpowers/specs/2026-10-17-issue-129-s1-callgraph-and-baseline.md and is expected to
/// turn green in the slice that owns that gap. Do not relax an assertion to make it pass: the
/// assertion is the contract.
/// </summary>
public class TicketProjectionGapBaselineTests : BaseTest
{
    public TicketProjectionGapBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void R01_unit_of_work_provider_binding_is_registered_exactly_once()
    {
        var bindings = Factory.Services.GetServices<UnitOfWorkProviderBinding>()
            .Where(binding => binding.Provider == OrmProvider.EfCore)
            .ToList();

        bindings.Should().HaveCount(1,
            "the #137 single-assembly rule allows exactly one EfCore binding; the platform preset, " +
            "the sample EntityFrameworkCoreModule and the test fixture each register one, so the registry " +
            "throws on the first write and no Ticket write action can be proven today");
    }

    [Fact]
    public void R02_ticket_permission_names_used_by_the_projection_are_defined_permissions()
    {
        var manager = Factory.Services.GetRequiredService<IPermissionDefinitionManager>();

        foreach (var name in new[]
                 {
                     "Ticket.Create", "Ticket.Get", "Ticket.Update", "Ticket.Delete",
                     "Ticket.Search", "Ticket.GetAll",
                 })
        {
            manager.GetOrNull(name).Should().NotBeNull(
                $"the published Ticket endpoints require {name}, but no definition provider declares it, " +
                "so only a super admin can ever call them and grant/revocation coverage cannot be tested");
        }
    }

    [Fact]
    public async Task R03_malformed_json_body_is_rejected_with_400_before_business_execution()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var response = await client.PostAsync("/api/ticket",
            new StringContent("{not json", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a malformed request body is a client error; the raw JsonException currently reaches the " +
            "converter's catch-all and is reported as Crest.InternalError/500");
    }

    [Fact]
    public async Task R04_malformed_guid_input_is_rejected_with_400_before_business_execution()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var route = await client.GetAsync("/api/ticket/not-a-guid");
        var query = await client.GetAsync("/api/ticket/close?id=not-a-guid");
        var missing = await client.GetAsync("/api/ticket/assign?id=" + Guid.NewGuid());

        foreach (var response in new[] { route, query, missing })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "Guid.Parse/FormatException on user-supplied route or query values currently falls through " +
                "to Crest.InternalError/500 instead of a 400 request error");
        }
    }

    [Fact]
    public async Task R05_update_with_a_stale_concurrency_stamp_is_rejected_and_writes_nothing()
    {
        using var host = WithSingleUnitOfWorkBinding();
        var (client, _) = await SignInAsync(host);
        var ticketId = await CreateTicketAsync(client);

        var response = await client.PutAsync($"/api/ticket/{ticketId}", new StringContent(
            "{\"title\":\"stale stamp overwrite attempt\",\"description\":\"stale stamp overwrite description\"," +
            "\"priority\":3,\"type\":2,\"categoryId\":null,\"concurrencyStamp\":\"00000000-0000-0000-0000-000000000000\"}",
            Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "CrestAppServiceBase.UpdateAsync restores the stored ConcurrencyStamp after mapping and never " +
            "compares the client value, so a stale update silently overwrites the row");

        var read = await client.GetAsync($"/api/ticket/{ticketId}");
        using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("data").GetProperty("title").GetString()
            .Should().NotBe("stale stamp overwrite attempt", "a rejected update must not change persisted state");
    }

    [Fact]
    public async Task R06_delete_without_if_match_is_rejected_with_428_and_deletes_nothing()
    {
        using var host = WithSingleUnitOfWorkBinding();
        var (client, _) = await SignInAsync(host);
        var ticketId = await CreateTicketAsync(client);

        var response = await client.DeleteAsync($"/api/ticket/{ticketId}");

        response.StatusCode.Should().Be((HttpStatusCode)428,
            "Ticket implements IHasConcurrencyStamp but DeleteAsync falls through to an unconditional delete " +
            "when no If-Match value is supplied");
    }

    [Fact]
    public async Task R07_ticket_read_contracts_expose_the_current_concurrency_stamp()
    {
        using var host = WithSingleUnitOfWorkBinding();
        var (client, _) = await SignInAsync(host);
        var ticketId = await CreateTicketAsync(client);

        var response = await client.GetAsync($"/api/ticket/{ticketId}");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("data").TryGetProperty("concurrencyStamp", out var stamp).Should().BeTrue(
            "the read contract must expose the stamp the If-Match precondition is written against");
        stamp.ValueKind.Should().Be(JsonValueKind.String);
        stamp.GetString().Should().NotBeNullOrWhiteSpace(
            "TicketAppService.MapToDto never copies ConcurrencyStamp, so every read returns an empty stamp " +
            "and no client can ever satisfy the optimistic-concurrency contract");
    }

    [Fact]
    public async Task R08_declared_dto_validator_runs_before_the_domain_rule()
    {
        using var host = WithSingleUnitOfWorkBinding();
        var (client, _) = await SignInAsync(host);
        var customerId = await NewCustomerIdAsync();

        var response = await client.PostAsync("/api/ticket", new StringContent(
            $"{{\"title\":\"ab\",\"description\":\"long enough description here\",\"priority\":2,\"type\":2,\"customerId\":\"{customerId}\"}}",
            Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be("Crest.Validation.Failed",
            "CreateTicketDtoValidator requires a 5-200 character title, but the request is accepted by the " +
            "validation step and only fails later in Ticket.SetTitle, which proves the declared validator never ran");
    }

    private WebApplicationFactory<global::Program> WithSingleUnitOfWorkBinding()
        => Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Removes the R01 registration defect so that the assertions above measure their own
            // single cause instead of the unit-of-work assembly failure.
            foreach (var duplicate in services.Where(d => d.ServiceType == typeof(UnitOfWorkProviderBinding)).Skip(1).ToList())
            {
                services.Remove(duplicate);
            }
        }));

    private async Task<string> NewCustomerIdAsync()
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        return (await HelpdeskTestData.CreateCustomerAsync(scopeFactory)).ToString();
    }

    private async Task<string> CreateTicketAsync(HttpClient client)
    {
        var customerId = await NewCustomerIdAsync();
        var payload = """
            {"title":"gap baseline ticket title","description":"gap baseline ticket description",
             "priority":2,"type":2,"customerId":"@@CID@@"}
            """;
        var response = await client.PostAsync("/api/ticket", new StringContent(payload.Replace("@@CID@@", customerId), Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<(HttpClient Client, string AccessToken)> SignInAsync(WebApplicationFactory<global::Program> factory)
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
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("access_token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, token);
    }
}
