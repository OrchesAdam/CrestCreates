using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using SaaSHelpdesk.EntityFrameworkCore;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 S1 red baseline (rev.2 §14.7.4). Each test fails for exactly one capability gap
/// recorded in docs/superpowers/specs/2026-10-17-issue-129-s1-callgraph-and-baseline.md.
/// Assertions pair a status/code expectation with a side-effect expectation, so a later fix cannot
/// pass by returning the right status while still touching the database, and an existing green
/// cannot hide an unexecuted business call. Do not relax an assertion to make a test pass.
/// </summary>
public class TicketProjectionGapBaselineTests : BaseTest
{
    private readonly TicketWriteSideEffectProbe _probe = new();

    public TicketProjectionGapBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void R01_unit_of_work_provider_binding_is_registered_exactly_once()
    {
        var bindings = Factory.Services.GetServices<CrestCreates.Data.Abstractions.UnitOfWorkProviderBinding>()
            .Where(binding => binding.Provider == CrestCreates.Data.Abstractions.OrmProvider.EfCore)
            .ToList();

        bindings.Should().HaveCount(1,
            "the #137 single-assembly rule allows exactly one EfCore binding. The platform preset and the " +
            "sample persistence module each declare one; the fixture previously added a third and no longer " +
            "does (issue #129 rev.2 14.3). Ownership must be resolved by composition, not by making the " +
            "registry silently keep the first binding");
    }

    [Fact]
    public async Task R03_malformed_json_body_is_rejected_with_400_and_touches_nothing()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var before = await CountTicketsAsync();
        _probe.Reset();

        var response = await client.PostAsync("/api/ticket",
            new StringContent("{not json", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a malformed request body is a client error; the raw JsonException currently reaches the " +
            "converter's catch-all and is reported as Crest.InternalError/500");
        _probe.ServiceCalls.Should().BeEmpty("the handler must not reach the business service");
        _probe.UnitOfWorkExecutions.Should().Be(0, "no write transaction may be started");
        (await CountTicketsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task R04_malformed_or_missing_guid_input_is_rejected_with_400_and_calls_no_service()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        _probe.Reset();

        var route = await client.GetAsync("/api/ticket/not-a-guid");
        var badQuery = await client.GetAsync("/api/ticket/close?id=not-a-guid");
        var missingQuery = await client.GetAsync("/api/ticket/assign?id=" + Guid.NewGuid());

        _probe.ServiceCalls.Should().BeEmpty(
            "route and query binding failures must be produced by the typed binder, before any service call");
        foreach (var response in new[] { route, badQuery, missingQuery })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "Guid.Parse/FormatException on user-supplied input currently falls through to " +
                "Crest.InternalError/500 instead of a 400 request error");
        }
    }

    [Fact]
    public async Task R05_update_with_a_stale_concurrency_stamp_is_rejected_and_persists_nothing()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        var original = await ReadTicketAsync(ticketId);
        _probe.Reset();

        var response = await client.PutAsync($"/api/ticket/{ticketId}", new StringContent(
            StaleUpdatePayload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "CrestAppServiceBase.UpdateAsync restores the stored ConcurrencyStamp after mapping and never " +
            "compares the client value, so a stale update silently overwrites the row");

        var after = await ReadTicketAsync(ticketId);
        after.Title.Should().Be(original.Title, "a rejected update must not change persisted state");
        after.ConcurrencyStamp.Should().Be(original.ConcurrencyStamp, "a rejected update must not rotate the stamp");
    }

    [Fact]
    public async Task R06_delete_without_if_match_is_rejected_with_428_and_keeps_the_row()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        var original = await ReadTicketAsync(ticketId);
        _probe.Reset();

        var response = await client.DeleteAsync($"/api/ticket/{ticketId}");

        response.StatusCode.Should().Be((HttpStatusCode)428,
            "Ticket implements IHasConcurrencyStamp but DeleteAsync falls through to an unconditional delete " +
            "when no If-Match value is supplied");

        var after = await ReadTicketAsync(ticketId);
        after.Exists.Should().BeTrue("the row must still exist after a rejected delete");
        after.ConcurrencyStamp.Should().Be(original.ConcurrencyStamp);

        var second = await client.DeleteAsync($"/api/ticket/{ticketId}");
        second.StatusCode.Should().Be((HttpStatusCode)428,
            "the rejection must not be a one-shot state change; the row is still addressable");
    }

    [Fact]
    public async Task R07_read_contracts_return_the_current_persisted_stamp()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        var persisted = await ReadTicketAsync(ticketId);

        var response = await client.GetAsync($"/api/ticket/{ticketId}");
        var served = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("concurrencyStamp").GetString();

        served.Should().NotBeNullOrWhiteSpace(
            "TicketAppService.MapToDto never copies ConcurrencyStamp, so every read serves an empty value and " +
            "no client can obtain the stamp the If-Match precondition is checked against");
        served.Should().Be(persisted.ConcurrencyStamp,
            "the served stamp must be the row's current stamp, not a constant");
    }

    [Fact]
    public async Task R08_declared_dto_validator_runs_before_the_service_and_persists_nothing()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var customerId = await NewCustomerIdAsync();
        var before = await CountTicketsAsync();
        _probe.Reset();

        var response = await client.PostAsync("/api/ticket", new StringContent(
            ShortTitlePayload(customerId), Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        _probe.ServiceCalls.Should().BeEmpty(
            "CreateTicketDtoValidator rejects a 2-character title, so the business service must not be entered");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be("Crest.Validation.Failed",
            "the request is accepted by the validation step today and only fails later inside Ticket.SetTitle, " +
            "which proves the declared validator never ran");
        (await CountTicketsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task P09_positive_control_update_with_the_current_stamp_persists_and_rotates_it()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        var persisted = await ReadTicketAsync(ticketId);
        _probe.Reset();

        var response = await client.PutAsync($"/api/ticket/{ticketId}", new StringContent(
            ValidUpdatePayload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _probe.ServiceCalls.Should().Contain(nameof(SaaSHelpdesk.Application.Contracts.Interfaces.ITicketAppService.UpdateAsync),
            "the probe must actually observe the business call, otherwise R05/R08 could pass on an empty counter");
        _probe.UnitOfWorkExecutions.Should().Be(1);

        var after = await ReadTicketAsync(ticketId);
        after.Exists.Should().BeTrue();
        after.ConcurrencyStamp.Should().NotBe(persisted.ConcurrencyStamp,
            "a successful write must produce a new persisted stamp so the next precondition can be checked");
    }

    [Fact]
    public async Task P10_positive_control_delete_with_the_current_if_match_removes_the_row()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        var persisted = await ReadTicketAsync(ticketId);
        _probe.Reset();

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/ticket/{ticketId}");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{persisted.ConcurrencyStamp}\""));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the accepted If-Match path must keep working; R06 only rejects the missing precondition");
        _probe.ServiceCalls.Should().Contain(nameof(SaaSHelpdesk.Application.Contracts.Interfaces.ITicketAppService.DeleteAsync));
        (await ReadTicketAsync(ticketId)).Exists.Should().BeFalse();
    }

    private WebApplicationFactory<global::Program> MeasurementHost()
        => Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .NormalizeDuplicateUnitOfWorkBindingsForMeasurement()
            .AddTicketSideEffectProbe(_probe)));

    private const string StaleUpdatePayload = """
        {"title":"stale stamp overwrite attempt","description":"stale stamp overwrite description",
         "priority":3,"type":2,"categoryId":null,"concurrencyStamp":"00000000-0000-0000-0000-000000000000"}
        """;

    private const string ValidUpdatePayload = """
        {"title":"accepted update title","description":"accepted update description",
         "priority":3,"type":2,"categoryId":null,"concurrencyStamp":"any-value-ignored-today"}
        """;

    private static string ShortTitlePayload(string customerId)
        => $$"""
           {"title":"ab","description":"long enough description here",
            "priority":2,"type":2,"customerId":"{{customerId}}"}
           """;

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
        var token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, token);
    }

    private async Task<Guid> CreateTicketAsync(HttpClient client)
    {
        var payload = $$"""
            {"title":"gap baseline ticket title","description":"gap baseline ticket description",
             "priority":2,"type":2,"customerId":"{{await NewCustomerIdAsync()}}"}
            """;
        var response = await client.PostAsync("/api/ticket", new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return Guid.Parse(JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("id").GetString()!);
    }

    private async Task<string> NewCustomerIdAsync()
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        return (await HelpdeskTestData.CreateCustomerAsync(scopeFactory)).ToString();
    }

    private async Task<(bool Exists, string Title, string ConcurrencyStamp)> ReadTicketAsync(Guid ticketId)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var ticket = await db.Tickets.FindAsync(ticketId);
        return ticket is null
            ? (false, string.Empty, string.Empty)
            : (true, ticket.Title, ticket.ConcurrencyStamp ?? string.Empty);
    }

    private async Task<int> CountTicketsAsync()
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        return await db.Tickets.CountAsync();
    }
}
