using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using AuditLogEntity = CrestCreates.Domain.AuditLog.AuditLog;
using FluentAssertions;
using CrestCreates.MultiTenancy.Abstract;
using LibraryManagement.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class AuditLogIntegrationTests : IClassFixture<LibraryManagementWebApplicationFactory>
{
    private const string HostTenantId = "host";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LibraryManagementWebApplicationFactory _factory;

    public AuditLogIntegrationTests(LibraryManagementWebApplicationFactory factory)
    {
        _factory = factory;
        // Trigger host initialization before any tests run
        _ = _factory.CreateClient();
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldReturnPagedResults()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Verify seed worked - query should return seeded data
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When
        var response = await client.GetAsync("/api/audit-log?pageIndex=0&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        envelope.Data!.TotalCount.Should().BeGreaterThanOrEqualTo(0);
        envelope.Data.Items.Should().NotBeNull();
        envelope.Data.PageIndex.Should().Be(0);
        envelope.Data.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldReturnEmpty_WhenNoAuditLogs()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When
        var response = await client.GetAsync("/api/audit-log?pageIndex=100&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldFilterByStatus()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - filter by status=0 (Success)
        var response = await client.GetAsync("/api/audit-log?status=0&pageIndex=0&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        // All returned items should have Status=0
        foreach (var item in envelope.Data!.Items)
        {
            item.Status.Should().Be(0);
        }
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldFilterByHttpMethod()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - filter by HttpMethod=GET
        var response = await client.GetAsync("/api/audit-log?httpMethod=GET&pageIndex=0&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        foreach (var item in envelope.Data!.Items)
        {
            item.HttpMethod.Should().Be("GET");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldContainExpectedFields()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When
        var response = await client.GetAsync("/api/audit-log?pageIndex=0&pageSize=1");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        envelope.Data!.Items.Should().NotBeEmpty("seed data should provide at least one audit log record");

        var first = envelope.Data.Items.First();
        first.Id.Should().NotBeEmpty();
        first.ExecutionTime.Should().NotBe(default(DateTime));
        first.ExecutionTime.Should().BeBefore(DateTime.UtcNow.AddMinutes(5));
        first.ExecutionTime.Should().BeAfter(DateTime.UtcNow.AddYears(-1));
        first.Duration.Should().BeGreaterThanOrEqualTo(0L);
        first.Status.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task AuditLog_GetList_TenantContext_ShouldOnlyReturnOwnTenantLogs()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - tenant-a context
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, "tenant-a");

        // When - query all logs without tenantId filter
        var response = await client.GetAsync("/api/audit-log?pageIndex=0&pageSize=100");

        // Then - should only see tenant-a logs, not host logs
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();

        foreach (var item in envelope.Data!.Items)
        {
            item.TenantId.Should().Be("tenant-a", "tenant context should only see own tenant's logs");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_TenantContext_ShouldNotBeAbleToQueryOtherTenantById()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - tenant-a context trying to query tenant-b (which doesn't exist in seed data, but host logs exist)
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, "tenant-a");

        // When - try to query host tenant logs by passing tenantId parameter
        var response = await client.GetAsync("/api/audit-log?tenantId=host&pageIndex=0&pageSize=100");

        // Then - should still only see tenant-a logs, tenantId filter should be ignored in tenant context
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();

        foreach (var item in envelope.Data!.Items)
        {
            item.TenantId.Should().Be("tenant-a", "tenant context should ignore tenantId filter and only return own logs");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_HostContext_ShouldBeAbleToQuerySpecificTenant()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context querying tenant-a logs
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - query tenant-a logs specifically
        var response = await client.GetAsync("/api/audit-log?tenantId=tenant-a&pageIndex=0&pageSize=100");

        // Then - should see only tenant-a logs
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        envelope.Data!.Items.Should().NotBeEmpty("host querying tenant-a should return seeded tenant-a audit logs");

        foreach (var item in envelope.Data!.Items)
        {
            item.TenantId.Should().Be("tenant-a", "host context with tenantId filter should return that tenant's logs");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_HostContext_ShouldBeAbleToQueryAllTenants()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context without tenant filter
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - query all logs
        var response = await client.GetAsync("/api/audit-log?pageIndex=0&pageSize=100");

        // Then - query should succeed and return properly structured response
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        envelope.Data!.Items.Should().NotBeEmpty("host query should return seeded audit logs across tenants");
        envelope.Data.Items.Should().Contain(item => item.TenantId == "host");
        envelope.Data.Items.Should().Contain(item => item.TenantId == "tenant-a");
    }

    private HttpClient CreateTenantClient(string tenantId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return client;
    }

    private async Task<(HttpClient Client, LoginResultResponse LoginResult)> CreateAuthenticatedClientAsync(
        string userName,
        string password,
        string tenantId)
    {
        var client = CreateTenantClient(tenantId);
        var loginResult = await LoginAsync(client, userName, password, tenantId);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.Token.AccessToken);

        if (tenantId == HostTenantId)
        {
            client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        }

        return (client, loginResult);
    }

    private async Task<LoginResultResponse> LoginAsync(
        HttpClient client,
        string userName,
        string password,
        string tenantId)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            password,
            tenantId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync<LoginResultResponse>(response);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private sealed class LoginResultResponse
    {
        public TokenResponse Token { get; set; } = new();
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
    }

    private sealed class DynamicApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class PagedResultResponse<T>
    {
        public T[] Items { get; set; } = Array.Empty<T>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class AuditLogResponse
    {
        public Guid Id { get; set; }
        public DateTime ExecutionTime { get; set; }
        public long Duration { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? TenantId { get; set; }
        public string? HttpMethod { get; set; }
        public string? Url { get; set; }
        public string? ServiceName { get; set; }
        public string? MethodName { get; set; }
        public int Status { get; set; }
        public string? ClientIpAddress { get; set; }
    }

    private sealed class CleanupAuditLogsRequest
    {
        public int? RetentionDays { get; set; }
        public DateTime? BeforeTime { get; set; }
    }

    private sealed class CleanupAuditLogsResult
    {
        public int DeletedCount { get; set; }
    }

    [Fact]
    public async Task AuditLog_Cleanup_ShouldDeleteLogs_OlderThanBeforeTime()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context
        var marker = $"cleanup-beforetime-{Guid.NewGuid():N}";
        var beforeTime = DateTime.UtcNow.AddMinutes(-2);

        await SeedAuditLogAsync("host", 0, beforeTime.AddMinutes(-10), $"/seed/{marker}/old-host");
        await SeedAuditLogAsync("tenant-a", 0, beforeTime.AddMinutes(-8), $"/seed/{marker}/old-tenant");
        await SeedAuditLogAsync("host", 0, beforeTime.AddMinutes(1), $"/seed/{marker}/recent-host");

        var targetCountBefore = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains(marker) && a.ExecutionTime < beforeTime));
        targetCountBefore.Should().Be(2, "the host cleanup test should seed two target rows in the deletion window");

        var countBefore = await CountAuditLogsAsync(query => query.Where(a => a.ExecutionTime < beforeTime));
        countBefore.Should().BeGreaterThan(0, "there should be deletable audit logs before cleanup");

        // When
        var cleanupResult = await ExecuteCleanupAsync(
            tenantId: null,
            new CleanupAuditLogsRequestDto { BeforeTime = beforeTime });

        var targetCountAfter = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains(marker) && a.ExecutionTime < beforeTime));
        var countAfter = await CountAuditLogsAsync(query => query.Where(a => a.ExecutionTime < beforeTime));
        targetCountAfter.Should().Be(0, "cleanup should delete the seeded target rows from the shared database");
        countAfter.Should().Be(0, "cleanup should remove every log older than the specified cutoff");
        cleanupResult.DeletedCount.Should().Be(countBefore,
            "deleted count should match the number of rows that satisfied the cleanup predicate before deletion");
        (await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/recent-host"))))
            .Should().Be(1, "logs newer than the cutoff should remain");
    }

    [Fact]
    public async Task AuditLog_Cleanup_ShouldDeleteLogs_ByRetentionDays()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context, get count before cleanup
        var marker = $"cleanup-retention-{Guid.NewGuid():N}";
        await SeedAuditLogAsync("host", 0, DateTime.UtcNow.AddMinutes(-5), $"/seed/{marker}/old-host");
        await SeedAuditLogAsync("host", 0, DateTime.UtcNow.AddMinutes(5), $"/seed/{marker}/future-host");

        var oldCountBefore = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains(marker) && a.ExecutionTime < DateTime.UtcNow));
        oldCountBefore.Should().Be(1, "the seeded old host log should exist before cleanup");

        // When - cleanup with RetentionDays=0 (deletes everything older than now, effectively all old logs)
        var cleanupResult = await ExecuteCleanupAsync(
            tenantId: null,
            new CleanupAuditLogsRequestDto { RetentionDays = 0 });
        cleanupResult.DeletedCount.Should().BeGreaterThan(0, "cleanup should delete at least the seeded old log");

        (await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/old-host"))))
            .Should().Be(0, "retention cleanup should remove logs older than now");
        (await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/future-host"))))
            .Should().Be(1, "logs newer than now should not be removed");
    }

    [Fact]
    public async Task AuditLog_Cleanup_ShouldReturnDeletedCount()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context
        var marker = $"cleanup-deleted-count-{Guid.NewGuid():N}";
        var beforeTime = DateTime.UtcNow.AddMinutes(-2);

        await SeedAuditLogAsync("host", 0, beforeTime.AddMinutes(-10), $"/seed/{marker}/old-host");
        await SeedAuditLogAsync("tenant-a", 0, beforeTime.AddMinutes(-9), $"/seed/{marker}/old-tenant");
        await SeedAuditLogAsync("host", 0, beforeTime.AddMinutes(1), $"/seed/{marker}/recent-host");

        var countBefore = await CountAuditLogsAsync(query => query.Where(a => a.ExecutionTime < beforeTime));
        countBefore.Should().BeGreaterThan(0);

        // When - cleanup with very old beforeTime
        var cleanupResult = await ExecuteCleanupAsync(
            tenantId: null,
            new CleanupAuditLogsRequestDto { BeforeTime = beforeTime });

        var countAfter = await CountAuditLogsAsync(query => query.Where(a => a.ExecutionTime < beforeTime));
        var actualDeleted = countBefore - countAfter;

        actualDeleted.Should().Be(cleanupResult.DeletedCount,
            "returned DeletedCount should match actual records removed");
    }

    [Fact]
    public async Task AuditLog_Cleanup_TenantContext_ShouldNotDeleteOtherTenantLogs()
    {
        await _factory.EnsureSeedCompleteAsync();
        var marker = $"cleanup-tenant-boundary-{Guid.NewGuid():N}";
        var beforeTime = DateTime.UtcNow.AddMinutes(-2);
        await SeedAuditLogAsync("host", 0, beforeTime.AddMinutes(-10), $"/seed/{marker}/host-old");
        await SeedAuditLogAsync("tenant-a", 0, beforeTime.AddMinutes(-9), $"/seed/{marker}/tenant-old");

        var hostCountBefore = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/host-old")));
        hostCountBefore.Should().Be(1);
        var tenantCountBefore = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/tenant-old")));
        tenantCountBefore.Should().Be(1);
        var targetCountBefore = hostCountBefore + tenantCountBefore;
        targetCountBefore.Should().Be(2);

        // When
        var cleanupResult = await ExecuteCleanupAsync(
            tenantId: "tenant-a",
            new CleanupAuditLogsRequestDto { BeforeTime = beforeTime });

        var tenantCountAfter = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/tenant-old")));
        tenantCountAfter.Should().Be(0, "tenant-a cleanup should delete tenant-a target rows from the shared database");
        tenantCountAfter.Should().BeLessThan(tenantCountBefore, "tenant-a old logs should be deleted");

        var hostCountAfter = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/host-old")));
        hostCountAfter.Should().Be(hostCountBefore, "host logs should NOT be affected by tenant-a cleanup");
        cleanupResult.DeletedCount.Should().BeGreaterThanOrEqualTo(tenantCountBefore);
    }

    [Fact]
    public async Task AuditLog_Cleanup_HostContext_ShouldBeAbleToCleanupAcrossTenants()
    {
        await _factory.EnsureSeedCompleteAsync();
        var marker = $"cleanup-host-boundary-{Guid.NewGuid():N}";
        var beforeTime = DateTime.UtcNow.AddMinutes(-2);
        await SeedAuditLogAsync("host", 0, beforeTime.AddMinutes(-10), $"/seed/{marker}/host-old");
        await SeedAuditLogAsync("tenant-a", 0, beforeTime.AddMinutes(-9), $"/seed/{marker}/tenant-old");

        // Given - host context
        var hostCountBefore = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/host-old")));
        var tenantCountBefore = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/tenant-old")));

        var totalCountBefore = hostCountBefore + tenantCountBefore;
        totalCountBefore.Should().Be(2);

        // When
        var cleanupResult = await ExecuteCleanupAsync(
            tenantId: null,
            new CleanupAuditLogsRequestDto { BeforeTime = beforeTime });

        var hostCountAfter = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/host-old")));
        var tenantCountAfter = await CountAuditLogsAsync(query =>
            query.Where(a => a.Url != null && a.Url.Contains($"{marker}/tenant-old")));
        var totalCountAfter = hostCountAfter + tenantCountAfter;

        hostCountAfter.Should().Be(0, "host cleanup should delete host target rows from the shared database");
        tenantCountAfter.Should().Be(0, "host cleanup should delete tenant-a target rows from the shared database");
        totalCountAfter.Should().Be(0, "host cleanup should delete matching logs from all tenants");
        cleanupResult.DeletedCount.Should().BeGreaterThanOrEqualTo(totalCountBefore);
    }

    [Fact]
    public async Task AuditLog_EndToEnd_NormalRequest_Query_Cleanup_Flow()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - authenticated host client
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var marker = $"normal-e2e-{Guid.NewGuid():N}";

        // Use a fixed beforeTime in the past (same pattern as passing tests)
        var beforeTime = DateTime.UtcNow.AddMinutes(-2);

        // Seed TWO records: one OLDER than beforeTime (should be deleted), one NEWER (should remain)
        // This ensures the test is robust even if one seed fails
        var oldSeedTime = beforeTime.AddMinutes(-10); // 10 minutes before now, 8 minutes older than beforeTime
        var newSeedTime = beforeTime.AddMinutes(1);  // 1 minute after beforeTime (newer, should NOT be deleted)
        await SeedAuditLogAsync(HostTenantId, 0, oldSeedTime, $"http://local/{marker}/old");
        await SeedAuditLogAsync(HostTenantId, 0, newSeedTime, $"http://local/{marker}/new");

        // Verify OLD record exists and is in the deletion window (ExecutionTime < beforeTime)
        var oldCountBefore = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/old") && a.ExecutionTime < beforeTime));
        oldCountBefore.Should().Be(1, "seeded OLD audit should exist and be older than beforeTime");

        // Verify NEW record exists and is NOT in the deletion window (ExecutionTime >= beforeTime)
        var newCountBefore = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/new") && a.ExecutionTime >= beforeTime));
        newCountBefore.Should().Be(1, "seeded NEW audit should exist and be newer than beforeTime");

        // Cleanup with beforeTime (deletes records where ExecutionTime < beforeTime)
        var cleanupRequest = new CleanupAuditLogsRequest { BeforeTime = beforeTime };
        var cleanupResponse = await client.PostAsJsonAsync("/api/audit-log-cleanup/process-cleanup", cleanupRequest);
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.OK, await cleanupResponse.Content.ReadAsStringAsync());
        var cleanupResult = await ReadJsonAsync<DynamicApiResponse<CleanupAuditLogsResult>>(cleanupResponse);
        cleanupResult.Code.Should().Be(200);
        cleanupResult.Data!.DeletedCount.Should().Be(oldCountBefore, "deleted count should match the OLD records in the deletion window");

        // Verify OLD record is gone
        var oldCountAfter = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/old")));
        oldCountAfter.Should().Be(0, "seeded OLD audit record should be deleted");

        // Verify NEW record still exists
        var newCountAfter = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/new")));
        newCountAfter.Should().Be(1, "seeded NEW audit record should NOT be deleted");
    }

    [Fact]
    public async Task AuditLog_EndToEnd_ExceptionRequest_Query_Cleanup_Flow()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - authenticated host client
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var marker = $"fail-e2e-{Guid.NewGuid():N}";

        // Use a fixed beforeTime in the past (same pattern as passing tests)
        var beforeTime = DateTime.UtcNow.AddMinutes(-2);

        // Seed TWO failed (status=1) records: one OLDER than beforeTime (should be deleted), one NEWER (should remain)
        var oldSeedTime = beforeTime.AddMinutes(-10); // 10 minutes before now, 8 minutes older than beforeTime
        var newSeedTime = beforeTime.AddMinutes(1);  // 1 minute after beforeTime (newer, should NOT be deleted)
        await SeedAuditLogAsync(HostTenantId, 1, oldSeedTime, $"http://local/{marker}/old");
        await SeedAuditLogAsync(HostTenantId, 1, newSeedTime, $"http://local/{marker}/new");

        // Verify OLD failed record exists and is in the deletion window
        var oldCountBefore = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/old") && a.Status == 1 && a.ExecutionTime < beforeTime));
        oldCountBefore.Should().Be(1, "seeded OLD failed audit should exist and be older than beforeTime");

        // Verify NEW failed record exists and is NOT in the deletion window
        var newCountBefore = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/new") && a.Status == 1 && a.ExecutionTime >= beforeTime));
        newCountBefore.Should().Be(1, "seeded NEW failed audit should exist and be newer than beforeTime");

        // Cleanup with beforeTime (deletes records where ExecutionTime < beforeTime)
        var cleanupRequest = new CleanupAuditLogsRequest { BeforeTime = beforeTime };
        var cleanupResponse = await client.PostAsJsonAsync("/api/audit-log-cleanup/process-cleanup", cleanupRequest);
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.OK, await cleanupResponse.Content.ReadAsStringAsync());
        var cleanupResult = await ReadJsonAsync<DynamicApiResponse<CleanupAuditLogsResult>>(cleanupResponse);
        cleanupResult.Code.Should().Be(200);
        cleanupResult.Data!.DeletedCount.Should().Be(oldCountBefore, "deleted count should match the OLD failed records in the deletion window");

        // Verify OLD failed record is gone
        var oldCountAfter = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/old")));
        oldCountAfter.Should().Be(0, "seeded OLD failed audit record should be deleted");

        // Verify NEW failed record still exists
        var newCountAfter = await CountAuditLogsAsync(query => query.Where(a => a.Url != null && a.Url.Contains($"{marker}/new")));
        newCountAfter.Should().Be(1, "seeded NEW failed audit record should NOT be deleted");
    }

    private async Task SeedAuditLogAsync(string tenantId, int status, DateTime executionTime, string url)
    {
        using var scope = _factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var persistedTenantId = await NormalizeAuditTenantIdAsync(dbContext, tenantId);
        dbContext.AuditLogs.Add(new AuditLogEntity(Guid.NewGuid())
        {
            Duration = 42,
            UserId = $"{tenantId}-seed-user",
            UserName = $"{tenantId}-seed-user",
            TenantId = persistedTenantId,
            ClientIpAddress = "127.0.0.1",
            HttpMethod = "GET",
            Url = url,
            ServiceName = "SeedAuditService",
            MethodName = "SeedAsync",
            Status = status,
            ExecutionTime = executionTime,
            CreationTime = DateTime.UtcNow,
            TraceId = Guid.NewGuid().ToString("N"),
            ExtraProperties = new Dictionary<string, object>()
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<CleanupAuditLogsResultDto> ExecuteCleanupAsync(
        string? tenantId,
        CleanupAuditLogsRequestDto request)
    {
        using var scope = _factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var tenantScope = string.IsNullOrWhiteSpace(tenantId) ? null : currentTenant.Change(tenantId);
        var cleanupAppService = scope.ServiceProvider.GetRequiredService<IAuditLogCleanupAppService>();
        return await cleanupAppService.ProcessCleanupAsync(request);
    }

    private static async Task<string> NormalizeAuditTenantIdAsync(LibraryDbContext dbContext, string tenantId)
    {
        if (tenantId == HostTenantId)
        {
            return tenantId;
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(item => item.Name == tenantId);
        return tenant?.Id.ToString() ?? tenantId;
    }

    private async Task<int> CountAuditLogsAsync(Func<IQueryable<AuditLogEntity>, IQueryable<AuditLogEntity>> filter)
    {
        using var scope = _factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        return await filter(dbContext.AuditLogs.AsQueryable()).CountAsync();
    }
}
