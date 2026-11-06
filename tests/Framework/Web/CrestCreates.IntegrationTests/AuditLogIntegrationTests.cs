using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Domain.Permission;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
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

        // When - query with a keyword filter that matches no records
        var response = await client.GetAsync("/api/audit-log?keyword=__nonexistent_keyword_for_test__&pageIndex=0&pageSize=10");

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

        // Then - should see only tenant-a logs (or at least some results when filtering by tenant-a)
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();

        // All returned items should be for tenant-a
        foreach (var item in envelope.Data!.Items)
        {
            item.TenantId.Should().Be("tenant-a", "host context with tenantId filter should return only that tenant's logs");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_HostContext_ShouldBeAbleToQueryAllTenants()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context without tenant filter
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - query all logs with large page size
        var response = await client.GetAsync("/api/audit-log?pageIndex=0&pageSize=1000");
        var rawContent = await response.Content.ReadAsStringAsync();

        // Then - query should succeed and return properly structured response
        response.StatusCode.Should().Be(HttpStatusCode.OK, rawContent);
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        // The host context can see all audit logs (no tenant filtering)
        // Results may include login audit logs (null TenantId) and/or seeded logs (host/tenant-a)
        envelope.Data.Items.Should().NotBeEmpty($"host query should return audit logs. Response: {rawContent}");
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

    private async Task<(HttpClient Client, TokenResponse LoginResult)> CreateAuthenticatedClientAsync(
        string userName,
        string password,
        string tenantId)
    {
        var client = CreateTenantClient(tenantId);
        var loginResult = await LoginAsync(client, userName, password);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        if (tenantId == HostTenantId)
        {
            client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        }

        return (client, loginResult);
    }

    private async Task<TokenResponse> LoginAsync(
        HttpClient client,
        string userName,
        string password)
    {
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = userName,
            ["password"] = password,
            ["client_id"] = "test-client",
            ["scope"] = "openid profile email offline_access"
        });
        var response = await client.PostAsync("/connect/token", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync<TokenResponse>(response);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
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

}
