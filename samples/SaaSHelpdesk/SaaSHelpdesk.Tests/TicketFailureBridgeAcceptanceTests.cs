using FluentValidation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;
using SaaSHelpdesk.EntityFrameworkCore;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 H05/H06: the Web bridge owns the failure wire (400/404/500 sanitization) and the
/// generated invoker propagates the request token and fails the host composition when the declared
/// typed validator is not registered.
/// </summary>
public class TicketFailureBridgeAcceptanceTests : BaseTest
{
    private readonly TicketWriteSideEffectProbe _probe = new();

    public TicketFailureBridgeAcceptanceTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task H05_unknown_ticket_returns_the_web_404_contract()
    {
        var (client, _) = await SignInAsync(MeasurementHost());

        var response = await client.GetAsync($"/api/ticket/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("Crest.Entity.NotFound");
        document.RootElement.GetProperty("message").GetString().Should().Be("资源不存在。");
    }

    [Fact]
    public async Task H05_business_not_found_uses_the_same_web_404_contract()
    {
        var (client, _) = await SignInAsync(MeasurementHost());

        var response = await client.GetAsync(
            $"/api/ticket/assign?id={Guid.NewGuid()}&agentId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("Crest.Entity.NotFound",
            "a KeyNotFoundException from the business service must share the web 404 contract with a null read");
    }

    [Fact]
    public async Task H05_domain_failure_maps_to_400_and_preserves_the_stored_state()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        var close = await client.GetAsync($"/api/ticket/close?id={ticketId}");
        close.StatusCode.Should().Be(HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
        (await ReadStatusAsync(ticketId)).Should().Be(TicketStatus.Closed);

        var response = await client.GetAsync($"/api/ticket/resolve?id={ticketId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("Crest.Operation.Invalid");
        (await ReadStatusAsync(ticketId)).Should().Be(TicketStatus.Closed,
            "a rejected domain operation must not change persisted state");
    }

    [Fact]
    public async Task H05_unknown_failure_is_500_sanitized_and_recovers()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        _probe.Reset();
        _probe.ServiceFailure = new NotSupportedException("internal secret marker 41f2");

        var response = await client.GetAsync($"/api/ticket/{ticketId}");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("41f2").And.NotContain("NotSupportedException");
        using (var document = JsonDocument.Parse(body))
        {
            document.RootElement.GetProperty("code").GetString().Should().Be("Crest.InternalError");
            document.RootElement.GetProperty("message").GetString().Should().Be("服务器内部错误。");
            document.RootElement.GetProperty("details").ValueKind.Should().Be(JsonValueKind.Null);
        }

        _probe.Reset();
        var recovered = await client.GetAsync($"/api/ticket/{ticketId}");
        recovered.StatusCode.Should().Be(HttpStatusCode.OK, "the sanitized failure must not poison later requests");
    }

    [Fact]
    public async Task H06_request_token_reaches_the_business_service()
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        var stamp = await ReadStampAsync(ticketId);
        _probe.Reset();

        var response = await client.PutAsync($"/api/ticket/{ticketId}", new StringContent(
            $$"""
              {"title":"accepted update title","description":"accepted update description",
               "priority":3,"type":2,"categoryId":null,"concurrencyStamp":"{{stamp}}"}
              """,
            Encoding.UTF8,
            "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _probe.ServiceCalls.Should().Contain(nameof(
            SaaSHelpdesk.Application.Contracts.Interfaces.ITicketAppService.UpdateAsync));
        _probe.LastServiceToken.CanBeCanceled.Should().BeTrue(
            "the generated invoker must hand the request/kernel token to the business service");
        _probe.LastServiceToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task H06_missing_required_validator_fails_the_host_before_mapping()
    {
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.RemoveAll<IValidator<CreateTicketDto>>()));

        var failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        var text = failure.ToString();
        text.Should().Contain("CCHTTP007");
        text.Should().Contain("IValidator");
        text.Should().Contain("CreateTicketDto");
    }

    [Theory]
    [InlineData("json")]
    [InlineData("format")]
    [InlineData("default-oce")]
    [InlineData("third-oce")]
    [InlineData("live-request-oce")]
    [InlineData("unknown-control")]
    public async Task ReviewS3_live_request_internal_failures_remain_sanitized_500(string mode)
    {
        var (client, _) = await SignInAsync(MeasurementHost());
        var ticketId = await CreateTicketAsync(client);
        _probe.Reset();
        using var third = new CancellationTokenSource();
        third.Cancel();
        _probe.ServiceFailure = mode switch
        {
            "json" => new JsonException("internal secret marker"),
            "format" => new FormatException("internal secret marker"),
            "default-oce" => new OperationCanceledException("internal secret marker"),
            "third-oce" => new OperationCanceledException("internal secret marker", third.Token),
            "live-request-oce" => null,
            _ => new NotSupportedException("internal secret marker"),
        };
        // The decorator can throw with the real, still-live request token after receiving it.
        _probe.ReviewThrowLiveTokenOce = mode == "live-request-oce";
        var response = await client.GetAsync($"/api/ticket/{ticketId}");
        var body = await response.Content.ReadAsStringAsync();
        _probe.ServiceCalls.Should().Equal(nameof(
            SaaSHelpdesk.Application.Contracts.Interfaces.ITicketAppService.GetByIdAsync));
        _probe.LastServiceToken.IsCancellationRequested.Should().BeFalse();
        body.Should().NotContain("internal secret marker");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Crest.InternalError");
        document.RootElement.GetProperty("details").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private WebApplicationFactory<global::Program> MeasurementHost()
        => Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddTicketSideEffectProbe(_probe)));

    private async Task<(HttpClient Client, string AccessToken)> SignInAsync(
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
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, token);
    }

    private async Task<Guid> CreateTicketAsync(HttpClient client)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        var customerId = await HelpdeskTestData.CreateCustomerAsync(scopeFactory);
        var payload = $$"""
            {"title":"failure bridge ticket title","description":"failure bridge ticket description",
             "priority":2,"type":2,"customerId":"{{customerId}}"}
            """;
        var response = await client.PostAsync("/api/ticket", new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return Guid.Parse(JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("id").GetString()!);
    }

    private async Task<TicketStatus> ReadStatusAsync(Guid ticketId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        return (await db.Tickets.AsNoTracking().SingleAsync(ticket => ticket.Id == ticketId)).Status;
    }

    private async Task<string> ReadStampAsync(Guid ticketId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        return (await db.Tickets.AsNoTracking().SingleAsync(ticket => ticket.Id == ticketId)).ConcurrencyStamp!;
    }
}
