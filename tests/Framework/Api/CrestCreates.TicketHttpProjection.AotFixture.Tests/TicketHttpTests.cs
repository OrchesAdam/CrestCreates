using System.Net;
using System.Text;
using System.Text.Json;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.TicketHttpProjection.AotFixture;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.TicketHttpProjection.AotFixture.Tests;

/// <summary>
/// JIT leg of the S5 fixture: the real generated Ticket bundle and real TicketAppService run over
/// the fixture's memory persistence leaf. The native gate is a separate, non-substitutable signal.
/// </summary>
public sealed class TicketHttpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TicketHttpTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ticket_http_surface_runs_on_the_formal_chain()
    {
        using var client = _factory.CreateClient();
        var store = _factory.Services.GetRequiredService<TicketMemoryStore>();
        var observer = _factory.Services.GetRequiredService<TicketBusinessObserver>();
        var checker = (FixturePermissionChecker)_factory.Services.GetRequiredService<IPermissionChecker>();
        var customerId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        var beforeCreate = observer.Capture();
        var created = await SendJsonAsync(client, HttpMethod.Post, "/api/ticket",
            CreateBody("JIT ticket", "JIT ticket description", customerId));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        observer.BusinessEntries.Should().Be(beforeCreate.BusinessEntries + 1);
        observer.UnitOfWorkBegins.Should().Be(beforeCreate.UnitOfWorkBegins + 1);
        observer.RepositoryWrites.Should().Be(beforeCreate.RepositoryWrites + 1);
        using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var data = createdDocument.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();
        var stamp = data.GetProperty("concurrencyStamp").GetString()!;

        (await client.GetAsync($"/api/ticket/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/ticket/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/ticket/all")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/ticket/by-customer/{customerId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await SendJsonAsync(client, HttpMethod.Put, $"/api/ticket/{id}",
            UpdateBody("JIT ticket updated", "JIT ticket updated description", customerId, stamp));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        using var updatedDocument = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        var updatedData = updatedDocument.RootElement.GetProperty("data");
        var updatedStamp = updatedData.GetProperty("concurrencyStamp").GetString()!;
        updatedStamp.Should().NotBe(stamp);

        var assigned = await client.GetAsync($"/api/ticket/assign?id={id}&agentId={agentId}");
        assigned.StatusCode.Should().Be(HttpStatusCode.OK);
        using var assignedDocument = JsonDocument.Parse(await assigned.Content.ReadAsStringAsync());
        var assignedData = assignedDocument.RootElement.GetProperty("data");
        assignedData.GetProperty("dueDate").ValueKind.Should().NotBe(JsonValueKind.Null);
        var assignedStamp = assignedData.GetProperty("concurrencyStamp").GetString()!;

        (await client.GetAsync($"/api/ticket/by-assignee/{agentId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/ticket/resolve?id={id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await client.GetAsync($"/api/ticket/close?id={id}");
        closed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var closedDocument = JsonDocument.Parse(await closed.Content.ReadAsStringAsync());
        var closedStamp = closedDocument.RootElement.GetProperty("data").GetProperty("concurrencyStamp").GetString()!;

        var before = store.TicketCount;
        var beforeInvalid = observer.Capture();
        var invalid = await SendJsonAsync(client, HttpMethod.Post, "/api/ticket",
            CreateBody("x", "short", customerId));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        store.TicketCount.Should().Be(before);
        observer.BusinessEntries.Should().Be(beforeInvalid.BusinessEntries);
        observer.UnitOfWorkBegins.Should().Be(beforeInvalid.UnitOfWorkBegins);
        observer.RepositoryWrites.Should().Be(beforeInvalid.RepositoryWrites);

        var deniedTicket = await SendJsonAsync(client, HttpMethod.Post, "/api/ticket",
            CreateBody("Denied JIT ticket", "Denied JIT ticket description", customerId));
        deniedTicket.StatusCode.Should().Be(HttpStatusCode.OK);
        using var deniedDocument = JsonDocument.Parse(await deniedTicket.Content.ReadAsStringAsync());
        var deniedId = deniedDocument.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        checker.Denied.Add("Ticket.Update");
        try
        {
            (await client.GetAsync($"/api/ticket/assign?id={deniedId}&agentId={agentId}"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            checker.Denied.Remove("Ticket.Update");
        }

        var deleted = new HttpRequestMessage(HttpMethod.Delete, $"/api/ticket/{id}");
        deleted.Headers.TryAddWithoutValidation("If-Match", $"\"{closedStamp}\"");
        (await client.SendAsync(deleted)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/ticket/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, string json)
        => client.SendAsync(new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private static string CreateBody(string title, string description, Guid customerId)
        => $$"""{"title":"{{title}}","description":"{{description}}","priority":2,"type":2,"customerId":"{{customerId}}"}""";

    private static string UpdateBody(string title, string description, Guid customerId, string stamp)
        => $$"""{"id":"{{Guid.Empty}}","title":"{{title}}","description":"{{description}}","status":1,"priority":2,"type":2,"customerId":"{{customerId}}","assigneeId":null,"categoryId":null,"dueDate":null,"resolvedAt":null,"closedAt":null,"isEscalated":false,"concurrencyStamp":"{{stamp}}"}""";
}
