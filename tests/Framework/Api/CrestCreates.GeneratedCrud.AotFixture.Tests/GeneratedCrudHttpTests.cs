using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CrestCreates.GeneratedCrud.AotFixture;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.GeneratedCrud.AotFixture.Tests;

/// <summary>
/// #128 S4 Step A: the five CRUD actions over real HTTP on a real Web Host whose scoped service is
/// the toolchain-generated WidgetAppService. Every request travels the generated ExplicitHost chain —
/// typed body binder → capability pipeline → generated invoker → generated AppService → memory
/// repository → typed result mapper/envelope — and the assertions check response content and store
/// state, not only status codes. [UnitOfWorkMo] is inert in this fixture (no weaving), which is
/// exactly why Step A can run without a UnitOfWork; Step B introduces it.
///
/// One negative case — unknown-id GET returning the declared 404 envelope — is kept as a pinned red
/// baseline: the generated path reaches the pipeline failure mapper (500), not the typed 404 branch.
/// See <see cref="WidgetGetById_WithUnknownId_Returns404Envelope"/> for the exact framework gap.
/// </summary>
public class GeneratedCrudHttpTests : IClassFixture<GeneratedCrudFixtureFactory>
{
    private readonly HttpClient _client;
    private readonly MemoryWidgetRepository _repository;

    public GeneratedCrudHttpTests(GeneratedCrudFixtureFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        _repository = factory.Services.GetRequiredService<MemoryWidgetRepository>();
    }

    [Fact]
    public async Task WidgetCreate_ViaGeneratedService_PersistsTheRowAndReturnsTheTypedEnvelope()
    {
        // Arrange — POST /api/widgets with the required CreateWidgetDto body.
        var name = UniqueName("create");

        // Act
        var response = await _client.PostAsync(
            "/api/widgets",
            JsonContent($$"""{"name":"{{name}}","price":12.5,"description":"fixture row"}"""));

        // Assert — typed 200 envelope over the created read contract.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        body.RootElement.GetProperty("message").GetString().Should().Be("操作成功");
        var data = body.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();
        id.Should().NotBe(Guid.Empty,
            "the persistence layer must issue the identity for a create through the generated service");
        data.GetProperty("name").GetString().Should().Be(name);
        data.GetProperty("price").GetDecimal().Should().Be(12.5m);
        data.GetProperty("description").GetString().Should().Be("fixture row");
        data.GetProperty("concurrencyStamp").GetString().Should().NotBeNullOrEmpty();

        var stored = _repository.Find(id);
        stored.Should().NotBeNull("the created row must be present in the memory repository");
        stored!.Name.Should().Be(name);
        stored.Price.Should().Be(12.5m);
    }

    [Fact]
    public async Task WidgetGetById_ViaGeneratedService_Returns200WithTheTypedEnvelope()
    {
        // Arrange — one row created through the same generated chain.
        var created = await CreateWidgetAsync("get");

        // Act — GET /api/widgets/{id}; the route id is parsed by the generated binder.
        var response = await _client.GetAsync($"/api/widgets/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(created.Id,
            "the route identity must arrive at the service and come back in the envelope");
        data.GetProperty("name").GetString().Should().Be(created.Name);
    }

    /// <summary>
    /// Spec §7.4 wire matrix: GET with an unknown id answers "不存在 404 + 无数据 envelope", and the
    /// generated contribution declares the void/404 envelope as a required JSON root.
    ///
    /// PINNED RED BASELINE (#128 S4 Step A defect, no assertion weakened): the generated
    /// WidgetAppService.GetByIdAsync throws <c>CrestEntityNotFoundException</c> instead of returning
    /// null, the Capability pipeline's catch-all turns any thrown exception into
    /// <c>CapabilityExecutionResult.Failure("PIPELINE_ERROR", …)</c>, and the endpoint failure mapper
    /// answers <c>Results.Problem</c> — i.e. HTTP 500, never the typed 404 envelope of the generated
    /// result mapper. In this reflection-free fixture host the 500 body itself additionally fails to
    /// serialize (<c>NotSupportedException: JsonTypeInfo metadata for type
    /// 'Microsoft.AspNetCore.Mvc.ProblemDetails' was not provided by TypeInfoResolver of type
    /// 'CrestCreates.Generated.CrestCreatesCrudJsonSerializerContext'</c>), so the response is an
    /// empty 500. The declared 404 branch is therefore unreachable through the generated service
    /// until the framework maps the domain not-found exception onto the declared wire contract.
    /// </summary>
    [Fact]
    public async Task WidgetGetById_WithUnknownId_Returns404Envelope()
    {
        // Arrange — an id no generated chain ever inserted.
        var unknown = Guid.NewGuid();

        // Act — the generated GetByIdAsync finds no entity for this id.
        var response = await _client.GetAsync($"/api/widgets/{unknown}");

        // Assert — the fail-closed 404 envelope (no data field).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "spec §7.4: an unknown id must answer 404, not a 500 from the pipeline failure mapper");
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(404);
        body.RootElement.GetProperty("message").GetString().Should().Be("资源不存在");
        body.RootElement.TryGetProperty("data", out _).Should().BeFalse(
            "the 404 envelope carries no data field");

        _repository.Find(unknown).Should().BeNull(
            "the unknown id must be absent from the store the generated service queried");
    }

    [Fact]
    public async Task WidgetSearch_ViaGeneratedQueryAdapter_Returns200PagedEnvelopeContainingTheCreatedRow()
    {
        // Arrange — one row created through the same generated chain.
        var created = await CreateWidgetAsync("search");

        // Act — POST /api/widgets/search with the finite wire filter shape (camelCase
        // filters[].field/operator/value; operator 2 is Contains).
        var response = await _client.PostAsync(
            "/api/widgets/search",
            JsonContent($$"""{"pageIndex":0,"pageSize":10,"filters":[{"field":"Name","operator":2,"value":"{{created.Name}}"}]}"""));

        // Assert — paged typed envelope containing exactly the created row.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("totalCount").GetInt32().Should().Be(1,
            "the unique name filter must match exactly the created row");
        data.GetProperty("pageIndex").GetInt32().Should().Be(0);
        data.GetProperty("pageSize").GetInt32().Should().Be(10);
        var items = data.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetGuid().Should().Be(created.Id);
        items[0].GetProperty("name").GetString().Should().Be(created.Name);
    }

    [Fact]
    public async Task WidgetUpdate_ViaGeneratedService_Returns200AndChangesTheStoredRow()
    {
        // Arrange — one row created through the same generated chain; the update body carries the
        // current concurrency stamp, exactly what the generated [Required] UpdateWidgetDto demands.
        var created = await CreateWidgetAsync("update");
        var newName = UniqueName("updated");

        // Act — PUT /api/widgets/{id}; the binder composes route id + body into the capability input.
        var response = await _client.PutAsync(
            $"/api/widgets/{created.Id}",
            JsonContent($$"""{"name":"{{newName}}","price":99.75,"description":null,"concurrencyStamp":"{{created.Stamp}}"}"""));

        // Assert — typed 200 envelope over the updated contract.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(created.Id);
        data.GetProperty("name").GetString().Should().Be(newName);
        data.GetProperty("price").GetDecimal().Should().Be(99.75m);
        data.GetProperty("concurrencyStamp").GetString().Should().NotBeNullOrEmpty();

        var stored = _repository.Find(created.Id);
        stored.Should().NotBeNull("the row must still exist after the update");
        stored!.Name.Should().Be(newName, "the update must be persisted, not only echoed");
        stored.Price.Should().Be(99.75m);
    }

    [Fact]
    public async Task WidgetDelete_ViaGeneratedService_Returns200VoidEnvelopeAndRemovesTheRow()
    {
        // Arrange — one row created through the same generated chain; DeleteAsync requires the
        // expected stamp on the entity with a concurrency contract.
        var created = await CreateWidgetAsync("delete");
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/widgets/{created.Id}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{created.Stamp}\"");

        // Act — DELETE /api/widgets/{id}; the binder composes route id + the If-Match header.
        var response = await _client.SendAsync(request);

        // Assert — the typed void envelope (no data field).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        body.RootElement.GetProperty("message").GetString().Should().Be("操作成功");
        body.RootElement.TryGetProperty("data", out _).Should().BeFalse(
            "Delete answers the typed void envelope");

        _repository.Find(created.Id).Should().BeNull(
            "the deleted row must be gone from the store the generated service wrote through");
    }

    private async Task<CreatedWidget> CreateWidgetAsync(string kind)
    {
        var name = UniqueName(kind);
        var response = await _client.PostAsync(
            "/api/widgets",
            JsonContent($$"""{"name":"{{name}}","price":3.25,"description":"{{kind}} row"}"""));
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the arrange step ('{0}') creates its row through the generated create chain", kind);

        using var body = await ReadDocumentAsync(response);
        var data = body.RootElement.GetProperty("data");
        return new CreatedWidget(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("name").GetString()!,
            data.GetProperty("concurrencyStamp").GetString()!);
    }

    private static string UniqueName(string kind) => $"widget-{kind}-{Guid.NewGuid():N}";

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed record CreatedWidget(Guid Id, string Name, string Stamp);
}

/// <summary>
/// Custom factory that sets the content root to the test project directory, required because the
/// test project lives in a deeply nested directory and default content-root auto-detection fails
/// (the same shape the proven CompatibilityProjection.E2E test factory uses).
/// </summary>
public sealed class GeneratedCrudFixtureFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectDir = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location)!))!;
        builder.UseContentRoot(projectDir);
    }
}
