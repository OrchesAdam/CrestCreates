using System.Net;
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
/// #128 S4 Step B: the generated write actions run under the unique UnitOfWork kernel. The Feature
/// assembly is woven (Rougamo.Fody), the host binds the fixture's memory provider through the #137
/// public contract, and these tests pin the three facts the plan requires on a real HTTP host:
/// a successful write begins and commits exactly one unit of work; a failure after the repository
/// write rolls back and discards the staged row; and a commit stays committed when the response
/// write fails (a response-side failure never masquerades as a rollback). The stale-stamp case pins
/// the optimistic-concurrency semantics through the same chain.
/// </summary>
public class GeneratedCrudUnitOfWorkTests : IClassFixture<GeneratedCrudFixtureFactory>
{
    private readonly HttpClient _client;
    private readonly MemoryWidgetRepository _repository;
    private readonly MemoryUnitOfWorkObserver _observer;
    private readonly ResponseWriteFailureGate _writeGate;

    public GeneratedCrudUnitOfWorkTests(GeneratedCrudFixtureFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        _repository = factory.Services.GetRequiredService<MemoryWidgetRepository>();
        _observer = factory.Services.GetRequiredService<MemoryUnitOfWorkObserver>();
        _writeGate = factory.Services.GetRequiredService<ResponseWriteFailureGate>();
    }

    [Fact]
    public async Task A_successful_write_runs_under_exactly_one_unit_of_work_and_commits()
    {
        // Arrange — the woven frame's facts before the request.
        var before = _observer.Capture();
        var name = UniqueName("uow-commit");

        // Act — a normal create through the generated service.
        var response = await _client.PostAsync(
            "/api/widgets",
            JsonContent($$"""{"name":"{{name}}","price":1.5,"description":null}"""));

        // Assert — the response is the typed success envelope...
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetInt32().Should().Be(200);

        // ...and the woven frame began and committed exactly one unit of work and rolled back none.
        var after = _observer.Capture();
        (after.BeginCount - before.BeginCount).Should().Be(1,
            "each write action must activate exactly one unit-of-work scope");
        (after.CommitCount - before.CommitCount).Should().Be(1,
            "a successful write must commit its unit of work");
        (after.RollbackCount - before.RollbackCount).Should().Be(0,
            "a successful write must never record a rollback");
        _repository.FindByName(name).Should().NotBeNull("the committed row must be observable in the store");
    }

    [Fact]
    public async Task A_write_that_fails_after_the_repository_write_rolls_back_and_discards_the_staged_row()
    {
        // Arrange — the fixture hook fails after Repository.InsertAsync already ran, so the rollback
        // has a real staged row to discard.
        var before = _observer.Capture();
        var name = "rollback-probe-" + Guid.NewGuid().ToString("N");

        // Act
        var response = await _client.PostAsync(
            "/api/widgets",
            JsonContent($$"""{"name":"{{name}}","price":2.5,"description":null}"""));

        // Assert — the failure surfaces as the serializable problem body...
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Contain("rollback probe");

        // ...and the unit of work rolled back instead of committing.
        var after = _observer.Capture();
        (after.BeginCount - before.BeginCount).Should().Be(1);
        (after.CommitCount - before.CommitCount).Should().Be(0,
            "a failed write must not commit");
        (after.RollbackCount - before.RollbackCount).Should().Be(1,
            "the failed write must roll back its unit of work");
        _repository.FindByName(name).Should().BeNull(
            "rollback must discard the row the repository had already written inside the window");
    }

    [Fact]
    public async Task A_committed_write_stays_committed_when_the_response_write_fails()
    {
        // Arrange — arm the response-write gate for exactly the next request.
        var before = _observer.Capture();
        var name = UniqueName("write-failure");
        _writeGate.ArmForNextWrite();

        // Act — the request fails while its response body is being written; the handler (and its
        // unit-of-work commit) has already completed by then.
        Exception? failure = null;
        HttpStatusCode? status = null;
        try
        {
            using var response = await _client.PostAsync(
                "/api/widgets",
                JsonContent($$"""{"name":"{{name}}","price":3.5,"description":null}"""));
            status = response.StatusCode;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        // Assert — the response side really failed (thrown write failure or a failing status)...
        (failure is not null || (status is not null && (int)status.Value >= 500)).Should().BeTrue(
            $"the armed gate must fail the response write (failure='{failure?.GetType().Name}', status={status})");

        // ...the unit of work committed and never rolled back...
        var after = _observer.Capture();
        (after.BeginCount - before.BeginCount).Should().Be(1);
        (after.CommitCount - before.CommitCount).Should().Be(1,
            "the unit of work had already committed before the response write failed");
        (after.RollbackCount - before.RollbackCount).Should().Be(0,
            "a response-side failure must not roll back a committed write");

        // ...and the committed fact survives: spec §7.3 — the response failure never masquerades as
        // a rollback.
        _repository.FindByName(name).Should().NotBeNull(
            "the committed row must remain in the store even though the response write failed");
    }

    [Fact]
    public async Task A_stale_concurrency_stamp_is_rejected_and_the_window_rolls_back_untouched()
    {
        // Arrange — create one row and then update it once, so the original stamp is stale.
        var created = await CreateWidgetAsync("stamp");
        var firstName = UniqueName("stamp-first");
        var firstUpdate = await _client.PutAsync(
            $"/api/widgets/{created.Id}",
            JsonContent($$"""{"name":"{{firstName}}","price":5.5,"description":null,"concurrencyStamp":"{{created.Stamp}}"}"""));
        firstUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var before = _observer.Capture();
        var staleName = UniqueName("stamp-stale");

        // Act — update again while presenting the already-rotated stamp.
        var response = await _client.PutAsync(
            $"/api/widgets/{created.Id}",
            JsonContent($$"""{"name":"{{staleName}}","price":6.5,"description":null,"concurrencyStamp":"{{created.Stamp}}"}"""));

        // Assert — the repository's optimistic-concurrency contract is what failed...
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Contain("Concurrency conflict");

        // ...the window rolled back without committing...
        var after = _observer.Capture();
        (after.BeginCount - before.BeginCount).Should().Be(1);
        (after.CommitCount - before.CommitCount).Should().Be(0);
        (after.RollbackCount - before.RollbackCount).Should().Be(1);

        // ...and the stored row is exactly the state the committed first update left.
        var stored = _repository.Find(created.Id);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be(firstName, "the rejected update must not change the stored row");
    }

    private async Task<CreatedWidget> CreateWidgetAsync(string kind)
    {
        var name = UniqueName(kind);
        var response = await _client.PostAsync(
            "/api/widgets",
            JsonContent($$"""{"name":"{{name}}","price":3.25,"description":"{{kind}} row"}"""));
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the arrange step ('{0}') creates its row through the generated create chain", kind);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        return new CreatedWidget(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("name").GetString()!,
            data.GetProperty("concurrencyStamp").GetString()!);
    }

    private static string UniqueName(string kind) => $"widget-{kind}-{Guid.NewGuid():N}";

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private sealed record CreatedWidget(Guid Id, string Name, string Stamp);
}
