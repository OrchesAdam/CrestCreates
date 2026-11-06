using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Retention;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.AuditRetention;
using CrestCreates.Application.Settings;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.Security.Abstractions;
using CrestCreates.Web.AuditRetention;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.EntityFrameworkCore;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// #132 P08 composition: the generated HTTP projection over the real audit retention admission,
/// with real identity, permission and tenant resolution. Every rejection must leave the provider
/// untouched; the success path only runs after an explicit permission grant and setting opt-in.
/// </summary>
public sealed class AuditRetentionHttpAdmissionTests : BaseTest
{
    private const string PlainUserPassword = "Agent123!";
    private const string StoreId = FakeRetentionStore.StoreIdValue;
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public AuditRetentionHttpAdmissionTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Rejections_never_touch_the_provider_and_granted_execution_succeeds()
    {
        var store = new FakeRetentionStore();
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSettingDefinitionProvider<AuditLoggingSettingDefinitionProvider>();
            services.AddAuditRetention();
            services.AddSingleton<IAuditRetentionStore>(store);
            services.AddCrestAuditRetentionHttp();
        }));

        using var anonymous = CreateTenantClient(host);
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/audit-retention/execute",
            new { storeId = StoreId, requestId = "http-anonymous", reasonCode = "retention.manual" });
        anonymousResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        store.DeleteCalls.Should().Be(0);

        var userId = await CreatePlainUserAsync(host.Services);
        using var client = await SignInAsPlainUserAsync(host, userId);
        var deniedResponse = await client.PostAsJsonAsync(
            "/api/audit-retention/execute",
            new { storeId = StoreId, requestId = "http-denied", reasonCode = "retention.manual" });
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var deniedExport = await client.PostAsJsonAsync(
            "/api/audit-retention/batches/export",
            new { storeId = StoreId, requestId = "http-denied", batchOrdinal = 1 });
        deniedExport.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        store.DeleteCalls.Should().Be(0);

        await GrantAsync(host.Services, userId, AuditRetentionPermissions.Execute);
        await EnableExecutionAsync(host.Services);

        var crossTenantResponse = await client.PostAsJsonAsync(
            "/api/audit-retention/execute",
            new
            {
                storeId = StoreId,
                tenantId = "another-tenant",
                requestId = "http-cross-tenant",
                reasonCode = "retention.manual"
            });
        crossTenantResponse.IsSuccessStatusCode.Should().BeFalse(
            await crossTenantResponse.Content.ReadAsStringAsync());
        store.DeleteCalls.Should().Be(0);

        var successResponse = await client.PostAsJsonAsync(
            "/api/audit-retention/execute",
            new { storeId = StoreId, requestId = "http-granted", reasonCode = "retention.manual" });
        successResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await successResponse.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await successResponse.Content.ReadAsStringAsync());
        var run = document.RootElement.GetProperty("data");
        run.GetProperty("state").GetString().Should().Be("completed");
        run.GetProperty("committedDeletedCount").GetInt64().Should().Be(1);
        run.GetProperty("requestId").GetString().Should().Be("http-granted");
        store.DeleteCalls.Should().Be(1);
        store.LastScopeKind.Should().Be(AuditRetentionScope.TenantKind);

        await GrantAsync(host.Services, userId, AuditRetentionPermissions.Export);
        var export = await client.PostAsJsonAsync(
            "/api/audit-retention/batches/export",
            new { storeId = StoreId, requestId = "http-granted", batchOrdinal = 1 });
        export.StatusCode.Should().Be(HttpStatusCode.OK, await export.Content.ReadAsStringAsync());
        using var exportDocument = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
        exportDocument.RootElement.GetProperty("data").GetProperty("status").GetString()
            .Should().NotBeNullOrWhiteSpace();
        var replay = await client.PostAsJsonAsync(
            "/api/audit-retention/batches/export",
            new { storeId = StoreId, requestId = "http-granted", batchOrdinal = 1 });
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        store.ReadBatchCalls.Should().Be(2);
    }

    [Fact]
    public async Task Preview_is_allowed_while_execution_stays_disabled()
    {
        var store = new FakeRetentionStore();
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSettingDefinitionProvider<AuditLoggingSettingDefinitionProvider>();
            services.AddAuditRetention();
            services.AddSingleton<IAuditRetentionStore>(store);
            services.AddCrestAuditRetentionHttp();
        }));

        var userId = await CreatePlainUserAsync(host.Services);
        using var client = await SignInAsPlainUserAsync(host, userId);
        await GrantAsync(host.Services, userId, AuditRetentionPermissions.Preview);

        var preview = await client.PostAsJsonAsync(
            "/api/audit-retention/preview",
            new { storeId = StoreId });
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        using var previewDocument = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        var previewBody = previewDocument.RootElement.GetProperty("data");
        previewBody.GetProperty("scopeKind").GetString().Should().Be(AuditRetentionScope.TenantKind);
        previewBody.GetProperty("eligibleInSample").GetInt32().Should().Be(2);
        store.PreviewCalls.Should().Be(1);

        await GrantAsync(host.Services, userId, AuditRetentionPermissions.Execute);
        var disabled = await client.PostAsJsonAsync(
            "/api/audit-retention/execute",
            new { storeId = StoreId, requestId = "http-disabled", reasonCode = "retention.manual" });
        disabled.IsSuccessStatusCode.Should().BeFalse(
            "execution stays opt-in until AuditLogging.Retention.Enabled is set");
        store.DeleteCalls.Should().Be(0);
    }

    private static HttpClient CreateTenantClient(WebApplicationFactory<global::Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", HostTenantId);
        return client;
    }

    private static async Task<string> CreatePlainUserAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tenantId = (await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == "host")).Id.ToString();
        var user = new User(Guid.NewGuid(), $"retention_{Guid.NewGuid():N}"[..18], $"retention_{Guid.NewGuid():N}@helpdesk.test", tenantId)
        {
            PasswordHash = hasher.HashPassword(PlainUserPassword),
            IsActive = true,
            IsSuperAdmin = false,
            CreationTime = DateTime.UtcNow,
            LastPasswordChangeTime = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id.ToString();
    }

    private static async Task<HttpClient> SignInAsPlainUserAsync(
        WebApplicationFactory<global::Program> host,
        string userId)
    {
        string userName;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            userName = (await db.Users.AsNoTracking().SingleAsync(user => user.Id == Guid.Parse(userId))).UserName;
        }

        var client = CreateTenantClient(host);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = userName,
            ["password"] = PlainUserPassword,
            ["client_id"] = "test-client",
            ["scope"] = "openid profile offline_access"
        }));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var accessToken = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task GrantAsync(IServiceProvider services, string userId, string permissionName)
        => await services.GetRequiredService<IPermissionGrantManager>().GrantAsync(new PermissionGrantInfo
        {
            PermissionName = permissionName,
            ProviderType = PermissionGrantProviderType.User,
            ProviderKey = userId,
            Scope = PermissionGrantScope.Global
        });

    private static async Task EnableExecutionAsync(IServiceProvider services)
        => await services.GetRequiredService<ISettingManager>()
            .SetGlobalAsync(AuditRetentionSettingNames.Enabled, bool.TrueString.ToLowerInvariant());

    private sealed class FakeRetentionStore : IAuditRetentionStore
    {
        public const string StoreIdValue = "postgresql-audit-retention";
        private const string SinkIdValue = "postgresql-runtime-audit";

        public AuditRetentionStoreIdentity Identity { get; } = new()
        {
            StoreId = StoreIdValue,
            SinkId = SinkIdValue,
            Capabilities = AuditRetentionStoreCapabilities.Full
        };

        public int DeleteCalls { get; private set; }
        public int PreviewCalls { get; private set; }
        public int ReadBatchCalls { get; private set; }
        public string? LastScopeKind { get; private set; }

        private readonly Dictionary<string, AuditRetentionRunRecord> _runs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AuditRetentionBatchReceipt> _receipts = new(StringComparer.Ordinal);
        public long LastCommittedBatchCount { get; private set; }

        public ValueTask<AuditRetentionRunRecord> CreateOrReadRunAsync(
            AuditRetentionRunRequest request,
            CancellationToken cancellationToken = default)
        {
            LastScopeKind = request.Scope.Kind;
            if (_runs.TryGetValue(request.Key.StableSignature(), out var existing))
                return ValueTask.FromResult(existing);
            var run = new AuditRetentionRunRecord
            {
                Key = request.Key,
                Policy = request.Policy,
                PolicyHash = request.Policy.ComputeHash(),
                SnapshotAt = request.SnapshotAt,
                Cutoff = request.Cutoff,
                ReasonCode = request.ReasonCode,
                Actor = request.Actor,
                CorrelationId = request.CorrelationId,
                State = AuditRetentionRunState.Running,
                CommittedDeletedCount = 0,
                CommittedBatchCount = 0,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            _runs[request.Key.StableSignature()] = run;
            return ValueTask.FromResult(run);
        }

        public ValueTask<AuditRetentionPreview> PreviewAsync(
            AuditRetentionPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            return ValueTask.FromResult(new AuditRetentionPreview
            {
                StoreId = StoreIdValue,
                SinkId = SinkIdValue,
                Scope = request.Scope,
                SnapshotAt = request.SnapshotAt,
                Cutoff = request.Cutoff,
                PolicyHash = request.Policy.ComputeHash(),
                BatchSize = request.Policy.BatchSize,
                MaxBatchesPerRun = request.Policy.MaxBatchesPerRun,
                HasMore = false,
                Sample =
                [
                    new AuditRetentionPreviewCandidate
                    {
                        AuditId = "http-preview-1",
                        OccurredAt = request.Cutoff.AddDays(-1),
                        FirstAcceptedAt = request.Cutoff.AddDays(-1),
                        Disposition = AuditRetentionCandidateDisposition.Eligible
                    },
                    new AuditRetentionPreviewCandidate
                    {
                        AuditId = "http-preview-2",
                        OccurredAt = request.Cutoff.AddDays(-2),
                        FirstAcceptedAt = request.Cutoff.AddDays(-2),
                        Disposition = AuditRetentionCandidateDisposition.Eligible
                    }
                ],
                EligibleInSample = 2,
                HeldInSample = 0,
                ProtectedInSample = 0,
                QuarantinedInSample = 0
            });
        }

        public ValueTask<AuditRetentionBatchOutcome> ExecuteBatchAsync(
            AuditRetentionBatchRequest request,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            LastScopeKind = request.Key.Scope.Kind;
            LastCommittedBatchCount = request.BatchOrdinal;
            var run = _runs[request.Key.StableSignature()] with
            {
                CommittedDeletedCount = 1,
                CommittedBatchCount = request.BatchOrdinal,
                State = AuditRetentionRunState.Completed,
                StopReason = AuditRetentionStopReasons.NoCandidates,
                UpdatedAt = Now
            };
            _runs[request.Key.StableSignature()] = run;
            _receipts[BatchKey(request.Key, request.BatchOrdinal)] = new AuditRetentionBatchReceipt
            {
                BatchOrdinal = request.BatchOrdinal,
                DeletedCount = 1,
                CommittedAt = Now
            };
            return ValueTask.FromResult(new AuditRetentionBatchOutcome
            {
                BatchOrdinal = request.BatchOrdinal,
                DeletedCount = 1,
                CommittedDeletedCount = 1,
                CommittedBatchCount = request.BatchOrdinal,
                RemainingMayExist = false,
                RunState = AuditRetentionRunState.Completed,
                StopReason = AuditRetentionStopReasons.NoCandidates,
                FromExistingReceipt = false,
                CommittedAt = Now
            });
        }

        public ValueTask<AuditRetentionRunRecord?> ReadRunAsync(
            AuditRetentionRunKey key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AuditRetentionRunRecord?>(
                _runs.TryGetValue(key.StableSignature(), out var run) ? run : null);

        public ValueTask<AuditRetentionBatchReceipt?> ReadBatchAsync(
            AuditRetentionRunKey key,
            long batchOrdinal,
            CancellationToken cancellationToken = default)
        {
            ReadBatchCalls++;
            return ValueTask.FromResult<AuditRetentionBatchReceipt?>(
                _receipts.TryGetValue(BatchKey(key, batchOrdinal), out var receipt) ? receipt : null);
        }

        private static string BatchKey(AuditRetentionRunKey key, long batchOrdinal)
            => $"{key.StableSignature()}\u001f{batchOrdinal}";

        public ValueTask<AuditRetentionRunRecord> FinalizeRunAsync(
            AuditRetentionRunFinalization finalization,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditHold> PlaceHoldAsync(
            AuditHoldPlacement placement,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditHold> ReleaseHoldAsync(
            AuditHoldRelease release,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
