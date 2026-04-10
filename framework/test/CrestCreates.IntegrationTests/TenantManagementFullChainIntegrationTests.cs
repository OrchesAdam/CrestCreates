using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class TenantManagementFullChainIntegrationTests : IClassFixture<LibraryManagementWebApplicationFactory>
{
    private const string HostTenantId = "host";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LibraryManagementWebApplicationFactory _factory;

    public TenantManagementFullChainIntegrationTests(LibraryManagementWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateTenant_ShouldAutoBootstrapAdminUserAndAllowLogin()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"test-tenant-{Guid.NewGuid():N}";
        var tenantDisplayName = $"测试租户 {tenantName}";
        var defaultAdminUserName = "admin";
        var defaultAdminPassword = "Admin123!";

        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenants", new
        {
            name = tenantName,
            displayName = tenantDisplayName,
            defaultConnectionString = _factory.ConnectionString
        });

        tenantResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await tenantResponse.Content.ReadAsStringAsync());

        var createdTenant = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenantResponse);
        createdTenant.Data.Should().NotBeNull();
        createdTenant.Data!.Name.Should().Be(tenantName);

        var tenantId = createdTenant.Data.Id.ToString();
        var adminEmail = $"admin@{tenantName.ToLowerInvariant()}.local";

        var tenantClient = CreateTenantClient(tenantId);
        var loginResult = await LoginAsync(tenantClient, defaultAdminUserName, defaultAdminPassword, tenantId);

        loginResult.Token.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginResult.Token.RefreshToken.Should().NotBeNullOrWhiteSpace();
        loginResult.User.UserName.Should().Be(defaultAdminUserName);
        loginResult.User.Email.Should().Be(adminEmail);
        loginResult.User.TenantId.Should().Be(tenantId);
        loginResult.User.IsSuperAdmin.Should().BeTrue();

        tenantClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.Token.AccessToken);

        var meResponse = await tenantClient.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentUser = await ReadJsonAsync<UserInfoResponse>(meResponse);
        currentUser.UserName.Should().Be(defaultAdminUserName);
        currentUser.TenantId.Should().Be(tenantId);
        currentUser.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task TenantResolution_WithHeaderTenantId_ShouldSetCurrentTenant()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"resolution-test-tenant-{Guid.NewGuid():N}";

        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenants", new
        {
            name = tenantName,
            displayName = "租户解析测试",
            defaultConnectionString = _factory.ConnectionString
        });

        tenantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdTenant = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenantResponse);
        var tenantId = createdTenant.Data!.Id.ToString();

        var defaultAdminUserName = "admin";
        var defaultAdminPassword = "Admin123!";
        var (tenantClient, loginResult) = await CreateAuthenticatedClientAsync(
            defaultAdminUserName, defaultAdminPassword, tenantId);

        tenantClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        tenantClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

        tenantClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.Token.AccessToken);

        var meResponse = await tenantClient.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentUser = await ReadJsonAsync<UserInfoResponse>(meResponse);
        currentUser.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task CrossTenantAccess_WithWrongTenantHeader_ShouldBeForbidden()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName1 = $"cross-tenant1-{Guid.NewGuid():N}";
        var tenantName2 = $"cross-tenant2-{Guid.NewGuid():N}";

        var tenant1Response = await adminClient.PostAsJsonAsync("/api/tenants", new
        {
            name = tenantName1,
            displayName = "跨租户1",
            defaultConnectionString = _factory.ConnectionString
        });
        tenant1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant1 = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenant1Response);
        var tenant1Id = tenant1.Data!.Id.ToString();

        var tenant2Response = await adminClient.PostAsJsonAsync("/api/tenants", new
        {
            name = tenantName2,
            displayName = "跨租户2",
            defaultConnectionString = _factory.ConnectionString
        });
        tenant2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant2 = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenant2Response);
        var tenant2Id = tenant2.Data!.Id.ToString();

        var defaultAdminUserName = "admin";
        var defaultAdminPassword = "Admin123!";
        var (tenant1Client, loginResult) = await CreateAuthenticatedClientAsync(
            defaultAdminUserName, defaultAdminPassword, tenant1Id);

        tenant1Client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        tenant1Client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant2Id);
        tenant1Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.Token.AccessToken);

        var meResponse = await tenant1Client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.Token.AccessToken);
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

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
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
        public UserInfoResponse User { get; set; } = new();
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public string TokenType { get; set; } = string.Empty;
    }

    private sealed class UserInfoResponse
    {
        public Guid Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? TenantId { get; set; }
        public bool IsSuperAdmin { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    private sealed class DynamicApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class TenantDtoResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
    }
}
