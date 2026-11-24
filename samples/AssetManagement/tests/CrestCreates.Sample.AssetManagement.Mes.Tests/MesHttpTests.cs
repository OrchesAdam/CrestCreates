using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Sample.AssetManagement.Contracts.Mes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;

namespace CrestCreates.Sample.AssetManagement.Mes.Tests;

public sealed class MesHttpTests
{
    private static readonly MesJsonContext Json = MesJsonContext.Default;

    [Fact]
    public async Task Exact_evidence_and_receipt_survive_restart_and_replay_after_expiry()
    {
        using var database = new TestDatabase();
        var path = LockPath(Guid.NewGuid(), "maintenance-01");
        var observationId = Guid.NewGuid();
        var observationPath = $"{path}/observations/{observationId}";
        var operationId = Guid.NewGuid();
        ProductionLockState locked;
        TrialObservation observation;
        ProductionReleaseReceipt receipt;
        await using (var host = await database.StartAsync())
        {
            using var client = host.Client();
            locked = await RegisterAsync(client, path);
            Assert.True(locked.IsLocked);
            var trial = new RecordTrialRequest(TrialOutcome.Passed, locked.RegisteredAt);
            observation = await RecordAsync(client, path, observationId, trial);
            Assert.NotEqual(Guid.Empty, observation.SourceEventId);
            Assert.Equal(observation, await SendOkAsync(client, HttpMethod.Get, observationPath, Json.TrialObservation));
            Assert.Equal(observation, await RecordAsync(client, path, observationId, trial with { ObservedAt = trial.ObservedAt.ToOffset(TimeSpan.FromHours(3)) }));
            using var changedTrial = await client.PutAsJsonAsync(observationPath, trial with { Outcome = TrialOutcome.Failed }, Json.RecordTrialRequest);
            await AssertErrorAsync(changedTrial, HttpStatusCode.Conflict, "observation_id_conflict");
            receipt = await ReleaseAsync(client, path, Request(operationId, observation));
            Assert.Equal(observation, receipt.Evidence);
        }

        await using var restarted = await database.StartAsync("00:00:00.0000001");
        using var restartedClient = restarted.Client();
        Assert.Equal(observation, await SendOkAsync(restartedClient, HttpMethod.Get, observationPath, Json.TrialObservation));
        Assert.Equal(receipt, await ReleaseAsync(restartedClient, path, Request(operationId, observation)));
        var state = await SendOkAsync(restartedClient, HttpMethod.Get, path, Json.ProductionLockState);
        Assert.False(state.IsLocked);
        Assert.Equal(locked with { Release = receipt }, state);
        Assert.Equal(state, await RegisterAsync(restartedClient, path));
        var changed = Request(operationId, observation) with { SourceEventId = Guid.NewGuid() };
        using var conflict = await PostReleaseAsync(restartedClient, path, changed);
        await AssertErrorAsync(conflict, HttpStatusCode.Conflict, "operation_id_conflict");
        using var secondOperation = await PostReleaseAsync(restartedClient, path, Request(Guid.NewGuid(), observation));
        await AssertErrorAsync(secondOperation, HttpStatusCode.Conflict, "lock_already_released");
    }

    [Fact]
    public async Task Repeated_registration_cannot_rebind_a_work_order_and_failed_releases_are_retryable()
    {
        using var database = new TestDatabase();
        await using var first = await database.StartAsync();
        await using var second = await database.StartAsync();
        using var one = first.Client();
        using var two = second.Client();
        var path = LockPath(Guid.NewGuid(), "stable-1");
        var conflicting = LockPath(Guid.NewGuid(), "stable-1");
        var registrations = await Task.WhenAll(
            RegisterAsync(one, path), RegisterAsync(two, path),
            PostRegisterConflictAsync(one, conflicting), PostRegisterConflictAsync(two, conflicting));
        Assert.Equal(registrations[0], registrations[1]);
        var evidence = await RecordAsync(one, path, Guid.NewGuid(), new(TrialOutcome.Failed, registrations[0].RegisteredAt));
        var request = Request(Guid.NewGuid(), evidence);
        var failures = await Task.WhenAll(PostReleaseAsync(one, path, request), PostReleaseAsync(two, path, request));
        foreach (var failure in failures)
            await AssertErrorAsync(failure, HttpStatusCode.Conflict, "evidence_failed");
        Assert.True((await SendOkAsync(two, HttpMethod.Get, path, Json.ProductionLockState)).IsLocked);
        var passed = await RecordAsync(one, path, Guid.NewGuid(), new(TrialOutcome.Passed, registrations[0].RegisteredAt));
        Assert.Equal(request.OperationId, (await ReleaseAsync(two, path, Request(request.OperationId, passed))).OperationId);
    }

    [Fact]
    public async Task Release_rejects_missing_failed_mismatched_and_expired_evidence_without_mutating_lock()
    {
        using var database = new TestDatabase();
        var path = LockPath(Guid.NewGuid(), "maintenance-02");
        var operation = Guid.NewGuid();
        TrialObservation passed;
        await using (var host = await database.StartAsync())
        {
            using var client = host.Client();
            var state = await RegisterAsync(client, path);
            var passedTrial = new RecordTrialRequest(TrialOutcome.Passed, state.RegisteredAt);
            using var preLock = await client.PutAsJsonAsync($"{path}/observations/{Guid.NewGuid()}",
                passedTrial with { ObservedAt = state.RegisteredAt.AddTicks(-1) }, Json.RecordTrialRequest);
            await AssertErrorAsync(preLock, HttpStatusCode.Conflict, "observation_time_outside_lock");
            using var future = await client.PutAsJsonAsync($"{path}/observations/{Guid.NewGuid()}",
                passedTrial with { ObservedAt = state.RegisteredAt.AddDays(1) }, Json.RecordTrialRequest);
            await AssertErrorAsync(future, HttpStatusCode.Conflict, "observation_time_outside_lock");
            passed = await RecordAsync(client, path, Guid.NewGuid(), passedTrial);
            var failed = await RecordAsync(client, path, Guid.NewGuid(), passedTrial with { Outcome = TrialOutcome.Failed });
            using var missing = await PostReleaseAsync(client, path, new(operation, Guid.NewGuid(), Guid.NewGuid()));
            await AssertErrorAsync(missing, HttpStatusCode.NotFound, "evidence_not_found");
            using var mismatched = await PostReleaseAsync(client, path, new(operation, passed.ObservationId, failed.SourceEventId));
            await AssertErrorAsync(mismatched, HttpStatusCode.NotFound, "evidence_not_found");
            using var failure = await PostReleaseAsync(client, path, Request(operation, failed));
            await AssertErrorAsync(failure, HttpStatusCode.Conflict, "evidence_failed");
            Assert.Equal(state, await SendOkAsync(client, HttpMethod.Get, path, Json.ProductionLockState));
        }

        await using (var strict = await database.StartAsync("00:00:00.0000001"))
        {
            using var client = strict.Client();
            using var expired = await PostReleaseAsync(client, path, Request(operation, passed));
            await AssertErrorAsync(expired, HttpStatusCode.Conflict, "evidence_outside_freshness_window");
            Assert.True((await SendOkAsync(client, HttpMethod.Get, path, Json.ProductionLockState)).IsLocked);
        }

        await using var regular = await database.StartAsync();
        using var retry = regular.Client();
        Assert.Equal(operation, (await ReleaseAsync(retry, path, Request(operation, passed))).OperationId);
    }

    [Fact]
    public async Task Bearer_identity_partitions_reads_and_releases_by_tenant_equipment_and_order()
    {
        using var database = new TestDatabase();
        await using var host = await database.StartAsync();
        var equipmentId = Guid.NewGuid();
        var path = LockPath(equipmentId, "maintenance-shared");
        var otherOrder = LockPath(equipmentId, "maintenance-other");
        var otherEquipment = LockPath(Guid.NewGuid(), "maintenance-different-equipment");
        using var anonymous = host.Client(token: null);
        anonymous.DefaultRequestHeaders.Add("X-Asset-Tenant", "tenant-a");
        using var unauthorized = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("Bearer", Assert.Single(unauthorized.Headers.WwwAuthenticate).Scheme);
        using var invalid = host.Client("wrong-token");
        using var forbidden = await invalid.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);

        using var tenantA = host.Client();
        tenantA.DefaultRequestHeaders.Add("X-Asset-Tenant", "tenant-b");
        using var standardOrder = await tenantA.PutAsJsonAsync(path, new RegisterProductionLockRequest(false), Json.RegisterProductionLockRequest);
        await AssertErrorAsync(standardOrder, HttpStatusCode.BadRequest, "critical_order_required");
        using var spoofedBody = new StringContent("""{"isCritical":true,"tenantId":"tenant-b"}""", System.Text.Encoding.UTF8, "application/json");
        using var spoofedTenant = await tenantA.PutAsync(path, spoofedBody);
        await AssertErrorAsync(spoofedTenant, HttpStatusCode.BadRequest, "invalid_json");
        using var nonexistent = await tenantA.GetAsync(path);
        await AssertErrorAsync(nonexistent, HttpStatusCode.NotFound, "lock_not_found");
        var state = await RegisterAsync(tenantA, path);
        Assert.Equal("tenant-a", state.TenantId);
        var evidence = await RecordAsync(tenantA, path, Guid.NewGuid(), new(TrialOutcome.Passed, state.RegisteredAt));
        using var tenantB = host.Client("sample-b");
        using var foreignLock = await tenantB.GetAsync(path);
        await AssertErrorAsync(foreignLock, HttpStatusCode.NotFound, "lock_not_found");
        Assert.Equal("tenant-b", (await RegisterAsync(tenantB, path)).TenantId);
        using var wrongEquipment = await tenantA.PutAsJsonAsync(LockPath(Guid.NewGuid(), "maintenance-shared"),
            new RegisterProductionLockRequest(true), Json.RegisterProductionLockRequest);
        await AssertErrorAsync(wrongEquipment, HttpStatusCode.Conflict, "work_order_equipment_conflict");
        await RegisterAsync(tenantA, otherOrder);
        await RegisterAsync(tenantA, otherEquipment);

        foreach (var (client, lockPath) in new[] { (tenantB, path), (tenantA, otherOrder), (tenantA, otherEquipment) })
        {
            using var foreignEvidence = await client.GetAsync($"{lockPath}/observations/{evidence.ObservationId}");
            await AssertErrorAsync(foreignEvidence, HttpStatusCode.NotFound, "evidence_not_found");
            using var rejected = await PostReleaseAsync(client, lockPath, Request(Guid.NewGuid(), evidence));
            await AssertErrorAsync(rejected, HttpStatusCode.NotFound, "evidence_not_found");
            Assert.True((await SendOkAsync(client, HttpMethod.Get, lockPath, Json.ProductionLockState)).IsLocked);
        }
    }

    [Fact]
    public async Task Concurrent_releases_share_one_receipt_and_cannot_rebind_operation_or_lock()
    {
        using var database = new TestDatabase();
        await using var first = await database.StartAsync();
        await using var second = await database.StartAsync();
        using var one = first.Client();
        using var two = second.Client();
        var path = LockPath(Guid.NewGuid(), "concurrent-1");
        var another = LockPath(Guid.NewGuid(), "concurrent-2");
        var third = LockPath(Guid.NewGuid(), "concurrent-3");
        var evidence = await RegisterAndPassAsync(one, path);
        var otherEvidence = await RegisterAndPassAsync(one, another);
        var thirdEvidence = await RegisterAndPassAsync(one, third);
        var request = Request(Guid.NewGuid(), evidence);
        var receipts = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => ReleaseAsync(index % 2 == 0 ? one : two, path, request)));
        Assert.All(receipts, receipt => Assert.Equal(receipts[0], receipt));
        var collision = await Task.WhenAll(
            PostReleaseAsync(one, another, Request(request.OperationId, otherEvidence)),
            PostReleaseAsync(two, another, Request(Guid.NewGuid(), otherEvidence)));
        using var reused = collision[0];
        await AssertErrorAsync(reused, HttpStatusCode.Conflict, "operation_id_conflict");
        using var accepted = collision[1];
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var competing = await Task.WhenAll(
            PostReleaseAsync(one, third, Request(Guid.NewGuid(), thirdEvidence)),
            PostReleaseAsync(two, third, Request(Guid.NewGuid(), thirdEvidence)));
        using var winner = Assert.Single(competing, response => response.IsSuccessStatusCode);
        using var loser = Assert.Single(competing, response => !response.IsSuccessStatusCode);
        await AssertErrorAsync(loser, HttpStatusCode.Conflict, "lock_already_released");
        Assert.Equal(await winner.Content.ReadFromJsonAsync(Json.ProductionReleaseReceipt),
            (await SendOkAsync(one, HttpMethod.Get, third, Json.ProductionLockState)).Release);
    }

    [Fact]
    public async Task Pending_events_survive_restart_and_acknowledgement_does_not_remove_observation()
    {
        using var database = new TestDatabase();
        var path = LockPath(Guid.NewGuid(), "event-source");
        RecordTrialRequest trial;
        TrialObservation failed;
        TrialObservation passed;
        await using (var host = await database.StartAsync())
        {
            using var client = host.Client();
            var state = await RegisterAsync(client, path);
            trial = new RecordTrialRequest(TrialOutcome.Passed, state.RegisteredAt);
            failed = await RecordAsync(client, path, Guid.NewGuid(), trial with { Outcome = TrialOutcome.Failed });
            passed = await RecordAsync(client, path, Guid.NewGuid(), trial);
            Assert.Equal(new[] { failed, passed }, await ReadEventsAsync(client));
            Assert.Equal(new[] { failed, passed }, await ReadEventsAsync(client));
        }

        // Recreate the pre-feed schema while preserving the original observation rows.
        database.RemoveAcknowledgementSchema();
        await using (var restarted = await database.StartAsync())
        {
            using var client = restarted.Client();
            Assert.Equal(failed, Assert.Single(await ReadEventsAsync(client, "?limit=1")));
            Assert.Equal(passed, Assert.Single(await ReadEventsAsync(client, $"?limit=1&after={failed.SourceEventId:D}")));
            Assert.Equal(failed, Assert.Single(await ReadEventsAsync(client, "?limit=1")));
            using var acknowledged = await AcknowledgeAsync(client, failed.SourceEventId);
            Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);
        }

        await using var afterAckRestart = await database.StartAsync();
        using var afterAckClient = afterAckRestart.Client();
        Assert.Equal(passed, Assert.Single(await ReadEventsAsync(afterAckClient)));
        Assert.Equal(passed, Assert.Single(await ReadEventsAsync(afterAckClient, $"?after={failed.SourceEventId:D}")));
        using var repeatedAck = await AcknowledgeAsync(afterAckClient, failed.SourceEventId);
        Assert.Equal(HttpStatusCode.NoContent, repeatedAck.StatusCode);
        Assert.Equal(failed, await RecordAsync(afterAckClient, path, failed.ObservationId, trial with { Outcome = TrialOutcome.Failed }));
        Assert.Equal(failed, await SendOkAsync(afterAckClient, HttpMethod.Get, $"{path}/observations/{failed.ObservationId:D}", Json.TrialObservation));
        Assert.Equal(passed, Assert.Single(await ReadEventsAsync(afterAckClient)));
        using var acknowledgedPassed = await AcknowledgeAsync(afterAckClient, passed.SourceEventId);
        Assert.Equal(HttpStatusCode.NoContent, acknowledgedPassed.StatusCode);
        Assert.Empty(await ReadEventsAsync(afterAckClient));
    }

    [Fact]
    public async Task Event_feed_and_acknowledgement_use_the_exact_bearer_tenant_and_source_event()
    {
        using var database = new TestDatabase();
        await using var host = await database.StartAsync();
        using var tenantA = host.Client();
        using var tenantB = host.Client("sample-b");
        using var anonymous = host.Client(token: null);
        var path = LockPath(Guid.NewGuid(), "tenant-event");
        var observationA = await RegisterAndPassAsync(tenantA, path);
        var observationB = await RegisterAndPassAsync(tenantB, path);

        anonymous.DefaultRequestHeaders.Add("X-Asset-Tenant", "tenant-a");
        using var anonymousRead = await anonymous.GetAsync(MesTrialEventFeed.Path);
        using var anonymousAck = await AcknowledgeAsync(anonymous, observationA.SourceEventId);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAck.StatusCode);
        tenantB.DefaultRequestHeaders.Add("X-Asset-Tenant", "tenant-a");
        Assert.Equal(observationB, Assert.Single(await ReadEventsAsync(tenantB)));

        using var wrongTenant = await AcknowledgeAsync(tenantB, observationA.SourceEventId);
        await AssertErrorAsync(wrongTenant, HttpStatusCode.NotFound, "trial_event_not_found");
        using var wrongIdentity = await AcknowledgeAsync(tenantA, observationA.ObservationId);
        await AssertErrorAsync(wrongIdentity, HttpStatusCode.NotFound, "trial_event_not_found");
        using var invalidIdentity = await AcknowledgeAsync(tenantA, Guid.Empty);
        await AssertErrorAsync(invalidIdentity, HttpStatusCode.BadRequest, "invalid_route_id");
        using var foreignCursor = await tenantB.GetAsync($"{MesTrialEventFeed.Path}?after={observationA.SourceEventId:D}");
        await AssertErrorAsync(foreignCursor, HttpStatusCode.NotFound, "trial_event_not_found");
        using var invalidCursor = await tenantA.GetAsync($"{MesTrialEventFeed.Path}?after=invalid");
        await AssertErrorAsync(invalidCursor, HttpStatusCode.BadRequest, "invalid_trial_event_cursor");
        using var invalidLimit = await tenantA.GetAsync($"{MesTrialEventFeed.Path}?limit={MesTrialEventFeed.MaximumBatchSize + 1}");
        await AssertErrorAsync(invalidLimit, HttpStatusCode.BadRequest, "invalid_trial_event_limit");
        Assert.Equal(observationA, Assert.Single(await ReadEventsAsync(tenantA)));
        Assert.Equal(observationB, Assert.Single(await ReadEventsAsync(tenantB)));
    }

    private static Task<TrialObservation[]> ReadEventsAsync(HttpClient client, string query = "") =>
        SendOkAsync(client, HttpMethod.Get, MesTrialEventFeed.Path + query, Json.TrialObservationArray);

    private static Task<HttpResponseMessage> AcknowledgeAsync(HttpClient client, Guid sourceEventId) =>
        client.PutAsync($"{MesTrialEventFeed.Path}/{sourceEventId:D}/ack", content: null);

    private static string LockPath(Guid equipmentId, string workOrderId) => $"api/mes/production-locks/{equipmentId}/{workOrderId}";

    private static ReleaseProductionLockRequest Request(Guid operationId, TrialObservation evidence) =>
        new(operationId, evidence.ObservationId, evidence.SourceEventId);

    private static async Task<TrialObservation> RegisterAndPassAsync(HttpClient client, string path)
    {
        var state = await RegisterAsync(client, path);
        return await RecordAsync(client, path, Guid.NewGuid(), new(TrialOutcome.Passed, state.RegisteredAt));
    }

    private static async Task<ProductionLockState> RegisterAsync(HttpClient client, string path)
    {
        using var response = await client.PutAsJsonAsync(path, new RegisterProductionLockRequest(true), Json.RegisterProductionLockRequest);
        return await ReadOkAsync(response, Json.ProductionLockState);
    }

    private static async Task<ProductionLockState> PostRegisterConflictAsync(HttpClient client, string path)
    {
        using var response = await client.PutAsJsonAsync(path, new RegisterProductionLockRequest(true), Json.RegisterProductionLockRequest);
        await AssertErrorAsync(response, HttpStatusCode.Conflict, "work_order_equipment_conflict");
        return null!;
    }

    private static async Task<TrialObservation> RecordAsync(HttpClient client, string path, Guid id, RecordTrialRequest request)
    {
        using var response = await client.PutAsJsonAsync($"{path}/observations/{id}", request, Json.RecordTrialRequest);
        return await ReadOkAsync(response, Json.TrialObservation);
    }

    private static Task<HttpResponseMessage> PostReleaseAsync(HttpClient client, string path, ReleaseProductionLockRequest request) =>
        client.PostAsJsonAsync($"{path}/release", request, Json.ReleaseProductionLockRequest);

    private static async Task<ProductionReleaseReceipt> ReleaseAsync(HttpClient client, string path, ReleaseProductionLockRequest request)
    {
        using var response = await PostReleaseAsync(client, path, request);
        return await ReadOkAsync(response, Json.ProductionReleaseReceipt);
    }

    private static async Task<T> SendOkAsync<T>(HttpClient client, HttpMethod method, string path, JsonTypeInfo<T> type) where T : class
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await client.SendAsync(request);
        return await ReadOkAsync(response, type);
    }

    private static async Task<T> ReadOkAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type) where T : class
    {
        response.EnsureSuccessStatusCode();
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync(type));
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(new MesError(code), await response.Content.ReadFromJsonAsync(Json.MesError));
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("crest-asset-mes-");
        private string DatabasePath => Path.Combine(_directory.FullName, "mes.db");

        public void RemoveAcknowledgementSchema()
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP INDEX trial_observations_pending_events;
                ALTER TABLE trial_observations DROP COLUMN event_acknowledged_at;
                """;
            command.ExecuteNonQuery();
        }

        public async Task<RunningHost> StartAsync(string maxAge = "00:30:00")
        {
            var app = Program.Build([
                "--urls=http://127.0.0.1:0",
                "--Logging:LogLevel:Default=Warning",
                $"--Mes:DatabasePath={DatabasePath}",
                $"--Mes:EvidenceMaxAge={maxAge}",
                "--Mes:Tenants:0:TenantId=tenant-a", "--Mes:Tenants:0:BearerToken=sample-a",
                "--Mes:Tenants:1:TenantId=tenant-b", "--Mes:Tenants:1:BearerToken=sample-b"
            ]);
            try
            {
                await app.StartAsync();
                return new RunningHost(app);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class RunningHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client(string? token = "sample-a")
        {
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            if (token is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
