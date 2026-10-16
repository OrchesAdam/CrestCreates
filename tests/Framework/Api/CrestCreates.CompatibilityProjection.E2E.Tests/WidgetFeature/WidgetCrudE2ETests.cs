using System.Net;
using System.Text;
using System.Text.Json;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.CompatibilityProjection.E2E;

/// <summary>
/// #128 S3-7 (J/H/Q, JIT leg): the five CRUD actions over real HTTP on a real
/// <see cref="WebApplicationFactory{TEntryPoint}"/> host carrying a CRUD-shaped ExplicitHost
/// activation. Every success request must travel binding → capability pipeline → contributed
/// invoker → scoped AppService → typed result mapper, and every rejection must stop before any
/// business call. The assertions check response content, not only status codes.
///
/// S3-7 part 2a (request-abort leg): the client-abort test proves a client-side abort reaches the
/// generated-shaped CRUD chain — the service receives the request's abort token, observes the
/// cancellation on it and the create never completes. Deliberately still open (not claimed here):
/// UoW commit vs response-write independence (lands with the S4 generated fixture, which runs a
/// real UoW through the generated service) and the generated-code (real toolchain) fixture itself —
/// the latter is S4's scope.
/// </summary>
public class WidgetCrudE2ETests : IClassFixture<CompatibilityProjectionE2ETestFactory>
{
    private readonly CompatibilityProjectionE2ETestFactory _factory;
    private readonly HttpClient _client;
    private readonly WidgetCallRecorder _recorder;

    public WidgetCrudE2ETests(CompatibilityProjectionE2ETestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        _recorder = factory.Services.GetRequiredService<WidgetCallRecorder>();
        _recorder.Reset();
    }

    [Fact]
    public async Task WidgetCreate_WithValidBody_Returns200WithTheCreatedEnvelope()
    {
        // Act — POST /api/widget with the required CreateWidgetDto body.
        var response = await _client.PostAsync("/api/widget", JsonContent("""{"title":"New Widget"}"""));

        // Assert — typed 200 envelope over the created read contract.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        body.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("title").GetString().Should().Be("New Widget",
            "the created read contract must carry the submitted title");
        data.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty,
            "the service-assigned identity must survive the typed result envelope");

        _recorder.CreateCalls.Should().Be(1, "the scoped AppService boundary is crossed exactly once");
        _recorder.LastCreateInput!.Title.Should().Be("New Widget");
        _recorder.LastCreateToken.CanBeCanceled.Should().BeTrue(
            "the invoker must pass the managed request token, not a default token");
    }

    [Fact]
    public async Task WidgetCreate_WithEmptyOrMissingBody_Returns400WithoutCallingTheService()
    {
        // Act — an empty body (Content-Length: 0) and a bodyless POST are both required-body failures.
        var emptyBody = await _client.PostAsync("/api/widget", new StringContent(string.Empty));
        var missingBody = await _client.PostAsync("/api/widget", content: null);

        // Assert — deterministic 400 before any business call.
        emptyBody.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the required-body reader rejects an empty Create body");
        missingBody.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the required-body reader rejects a missing Create body");
        _recorder.CreateCalls.Should().Be(0,
            "input rejection is zero-business: the scoped AppService must not be invoked");
    }

    [Fact]
    public async Task WidgetCreate_WithMalformedJson_Returns400WithoutCallingTheService()
    {
        // Act — invalid JSON cannot be deserialized as CreateWidgetDto.
        var response = await _client.PostAsync("/api/widget", JsonContent("""{"title":"""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "malformed JSON is a 400, never a pipeline or serialization error");
        _recorder.CreateCalls.Should().Be(0,
            "malformed-body rejection is zero-business");
    }

    [Fact]
    public async Task WidgetCreate_ClientAbortsTheRequest_ServiceObservesTheAbortTokenAndTheCreateDoesNotComplete()
    {
        // Arrange — arm the opt-in gate (the service will park inside CreateAsync after reporting
        // entry) and let this test own the abort token the way a disconnecting client owns the socket.
        _recorder.ArmCreateGate();
        using var abortSource = new CancellationTokenSource();

        // Act — start the POST without awaiting it; the request is in flight while the test holds
        // the abort token.
        var requestTask = _client.PostAsync(
            "/api/widget", JsonContent("""{"title":"Aborted Widget"}"""), abortSource.Token);

        // Wait (bounded polling) until the service reports gate entry. The pipeline cannot finish
        // while the gate is unreleased, so entry is a race-free precondition for the abort.
        await WaitForConditionAsync(
            () => _recorder.CreateEntered?.Task.IsCompleted == true,
            TimeSpan.FromSeconds(10),
            "the service to enter the armed create gate");

        // Abort the request exactly like a disconnecting client; the service is parked on the gate,
        // so the request abort token is the only way the parked await can complete.
        abortSource.Cancel();

        // Assert (a) — the request task settles as cancellation, never as a shaped response.
        var settled = await Task.WhenAny(requestTask, Task.Delay(TimeSpan.FromSeconds(10)));
        settled.Should().BeSameAs(requestTask,
            "the aborted request must settle instead of hanging or completing");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);

        // Assert (b) — the token handed down the generated-shaped chain is the request's abort
        // token: the service observed exactly that cancellation on its parked await.
        await WaitForConditionAsync(
            () => _recorder.CreateCancellationObserved,
            TimeSpan.FromSeconds(10),
            "the service to observe the request abort");
        _recorder.CreateCalls.Should().Be(1, "the entry recorded exactly one create invocation");
        _recorder.LastCreateToken.CanBeCanceled.Should().BeTrue();
        _recorder.LastCreateToken.IsCancellationRequested.Should().BeTrue(
            "the token the service received is the request's abort token, cancelled by the client abort");

        // Assert (c) — nothing beyond entry: the gate was never released, so the create cannot
        // have completed.
        _recorder.CreateCompleted.Should().BeFalse(
            "the operation must be cancelled rather than completed");
    }

    [Fact]
    public async Task WidgetGetById_WithKnownId_Returns200WithTheTypedEnvelope()
    {
        var id = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

        // Act — GET /api/widget/{id}; the route id is parsed by the contributed binder.
        var response = await _client.GetAsync($"/api/widget/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(id,
            "the route identity must arrive at the service and come back in the envelope");
        data.GetProperty("title").GetString().Should().Be("Existing Widget");

        _recorder.GetCalls.Should().Be(1);
        _recorder.LastGetId.Should().Be(id);
    }

    [Fact]
    public async Task WidgetGetById_WithUnknownId_Returns404VoidEnvelope()
    {
        // Act — the memory store answers null for this id.
        var response = await _client.GetAsync($"/api/widget/{WidgetAppService.UnknownWidgetId}");

        // Assert — the typed GET overload answers the fail-closed 404 envelope (no data field).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(404);
        body.RootElement.GetProperty("message").GetString().Should().Be("资源不存在");
        body.RootElement.TryGetProperty("data", out _).Should().BeFalse(
            "the 404 envelope carries no data field");

        _recorder.GetCalls.Should().Be(1, "the request reached the service before the null was mapped");
        _recorder.LastGetId.Should().Be(WidgetAppService.UnknownWidgetId);
    }

    [Fact]
    public async Task WidgetGetById_WithMalformedId_Returns400WithoutCallingTheService()
    {
        // Act — the route id cannot be parsed as the frozen identity type (Guid).
        var response = await _client.GetAsync("/api/widget/not-a-guid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a malformed route identity is a deterministic 400, never a pipeline error");
        _recorder.GetCalls.Should().Be(0,
            "route-identity rejection is zero-business");
    }

    [Fact]
    public async Task WidgetSearch_WithWireFilterPayload_Returns200PagedEnvelope()
    {
        // Act — POST /api/widget/search with the finite wire filter shape (camelCase
        // filters[].field/operator/value), exactly as CrudQueryFilterWireBaselineTests pins it.
        var response = await _client.PostAsync(
            "/api/widget/search",
            JsonContent("""{"pageIndex":0,"pageSize":10,"filters":[{"field":"Title","operator":2,"value":"widget"}]}"""));

        // Assert — paged typed envelope with items and counts.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("items").GetArrayLength().Should().Be(2);
        data.GetProperty("totalCount").GetInt32().Should().Be(2);
        data.GetProperty("pageIndex").GetInt32().Should().Be(0);

        _recorder.SearchCalls.Should().Be(1);
        var filters = _recorder.LastSearchInput!.Filters;
        filters.Should().ContainSingle("the filter payload must survive the typed body reader");
        filters![0].Field.Should().Be("Title");
        filters[0].Operator.Should().Be(FilterOperator.Contains,
            "operator 2 on the wire is Contains");
        filters[0].Value.Should().BeOfType<JsonElement>()
            .Which.GetString().Should().Be("widget",
                "the bounded wire value stays a constrained JsonElement on the descriptor");
    }

    [Fact]
    public async Task WidgetUpdate_CarriesRouteIdAndBodyIntoTheService()
    {
        var id = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

        // Act — PUT /api/widget/{id}; the binder composes route id + body into the capability input.
        var response = await _client.PutAsync($"/api/widget/{id}", JsonContent("""{"title":"Renamed"}"""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(id);
        data.GetProperty("title").GetString().Should().Be("Renamed");

        _recorder.UpdateCalls.Should().Be(1);
        _recorder.LastUpdateId.Should().Be(id,
            "the route id must compose into the capability input envelope");
        _recorder.LastUpdateInput!.Title.Should().Be("Renamed",
            "the update body must compose into the same envelope");
    }

    [Fact]
    public async Task WidgetDelete_CarriesRouteIdAndTrimmedIfMatchIntoTheService()
    {
        var id = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/widget/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"stamp-42\"");

        // Act — DELETE /api/widget/{id}; the binder composes route id + the If-Match header.
        var response = await _client.SendAsync(request);

        // Assert — the typed void envelope (no data field).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadDocumentAsync(response);
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        body.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        body.RootElement.TryGetProperty("data", out _).Should().BeFalse(
            "Delete answers the typed void envelope");

        _recorder.DeleteCalls.Should().Be(1);
        _recorder.LastDeleteId.Should().Be(id);
        _recorder.LastDeleteStamp.Should().Be("stamp-42",
            "the If-Match header joins the capability input envelope with the quotes trimmed");
    }

    [Fact]
    public void WidgetActivation_RegistersOneScopedInstanceForInterfaceAndConcrete()
    {
        // The AddGeneratedCrud-equivalent registration shape: the interface resolves to the same
        // scoped instance as the concrete AppService.
        using var scope = _factory.Services.CreateScope();
        var concrete = scope.ServiceProvider.GetRequiredService<WidgetAppService>();
        var contract = scope.ServiceProvider.GetRequiredService<IWidgetAppService>();

        contract.Should().BeSameAs(concrete,
            "the activation registers the interface and the concrete type on one scoped instance");
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>
    /// Bounded polling for fixture signals that have no awaitable completion (a flag or a TCS); a
    /// broken propagation becomes a definite <see cref="TimeoutException"/>, never a hang.
    /// </summary>
    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, string expectation)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalSeconds:0}s waiting for {expectation}.");
            }

            await Task.Delay(25);
        }
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
