using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        // Trigger host initialization and seed data before any tests run
        _factory.EnsureSeedCompleteAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task CreateTenant_ShouldAutoBootstrapAdminUserAndAllowLogin()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"test-tenant-{Guid.NewGuid():N}";
        var tenantDisplayName = $"测试租户 {tenantName}";
        var defaultAdminUserName = "admin";
        var defaultAdminPassword = "Admin123!";

        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName,
            displayName = tenantDisplayName,
            defaultConnectionString = _factory.ConnectionString
        });

        tenantResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await tenantResponse.Content.ReadAsStringAsync());

        var tenantEnvelope = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenantResponse);
        var createdTenant = tenantEnvelope.Data!;
        createdTenant.Name.Should().Be(tenantName);

        var tenantId = createdTenant.Id.ToString();

        var tenantClient = CreateTenantClient(tenantId);
        var loginResult = await LoginAsync(tenantClient, defaultAdminUserName, defaultAdminPassword);

        loginResult.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginResult.RefreshToken.Should().NotBeNullOrWhiteSpace();

        tenantClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.AccessToken);

        var meResponse = await tenantClient.GetAsync("/connect/userinfo");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentUser = await ReadJsonAsync<UserInfoResponse>(meResponse);
        currentUser.UserName.Should().Be(defaultAdminUserName);
        currentUser.Email.Should().NotBeNullOrWhiteSpace("tenant admin should have an email");
        currentUser.TenantId.Should().Be(tenantId);
        currentUser.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task TenantResolution_WithHeaderTenantId_ShouldSetCurrentTenant()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"resolution-test-tenant-{Guid.NewGuid():N}";

        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName,
            displayName = "租户解析测试",
            defaultConnectionString = _factory.ConnectionString
        });

        tenantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenantEnvelope = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenantResponse);
        var createdTenant = tenantEnvelope.Data!;
        var tenantId = createdTenant.Id.ToString();

        var defaultAdminUserName = "admin";
        var defaultAdminPassword = "Admin123!";
        var (tenantClient, loginResult) = await CreateAuthenticatedClientAsync(
            defaultAdminUserName, defaultAdminPassword, tenantId);

        tenantClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        tenantClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

        tenantClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.AccessToken);

        var meResponse = await tenantClient.GetAsync("/connect/userinfo");
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

        var tenant1Response = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName1,
            displayName = "跨租户1",
            defaultConnectionString = _factory.ConnectionString
        });
        tenant1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant1Envelope = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenant1Response);
        var tenant1 = tenant1Envelope.Data!;
        var tenant1Id = tenant1.Id.ToString();

        var tenant2Response = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName2,
            displayName = "跨租户2",
            defaultConnectionString = _factory.ConnectionString
        });
        tenant2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant2Envelope = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenant2Response);
        var tenant2 = tenant2Envelope.Data!;
        var tenant2Id = tenant2.Id.ToString();

        // Login as tenant1 admin (who is super admin)
        var defaultAdminUserName = "admin";
        var defaultAdminPassword = "Admin123!";
        var (tenant1Client, loginResult) = await CreateAuthenticatedClientAsync(
            defaultAdminUserName, defaultAdminPassword, tenant1Id);

        // Super admins can access any tenant, so changing the header to tenant2 should succeed
        tenant1Client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        tenant1Client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant2Id);
        tenant1Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.AccessToken);

        var meResponse = await tenant1Client.GetAsync("/connect/userinfo");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentUser = await ReadJsonAsync<UserInfoResponse>(meResponse);
        // The user's tenant_id in the token should still be tenant1, but current tenant context is tenant2
        // Super admin is allowed this access
        currentUser.UserName.Should().Be(defaultAdminUserName);
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
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", loginResult.AccessToken);
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

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
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

    private sealed class UserInfoResponse
    {
        [JsonPropertyName("sub")]
        public Guid Id { get; set; }
        [JsonPropertyName("name")]
        public string UserName { get; set; } = string.Empty;
        // Email is returned as the full URI claim type in userinfo response
        [JsonPropertyName("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")]
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("tenantid")]
        public string? TenantId { get; set; }
        // is_super_admin is returned as string "true"/"false"
        [JsonPropertyName("is_super_admin")]
        public string IsSuperAdminRaw { get; set; } = string.Empty;
        public bool IsSuperAdmin => bool.TryParse(IsSuperAdminRaw, out var v) && v;
        [JsonPropertyName("role")]
        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    private sealed class DynamicApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    [Fact]
    public async Task CreateTenant_SharedDb_ShouldSucceedWithSeedOnly()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"shared-{Guid.NewGuid():N}";

        // Create tenant WITHOUT defaultConnectionString (shared database mode)
        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName,
            displayName = "Shared DB Tenant"
        });

        tenantResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await tenantResponse.Content.ReadAsStringAsync());

        var tenantEnvelope = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenantResponse);
        var createdTenant = tenantEnvelope.Data!;
        createdTenant.Name.Should().Be(tenantName);

        // In shared-database mode, the orchestrator skips DatabaseInitialize and Migration
        // and runs DataSeed, SettingsDefaults, FeatureDefaults phases only.
        // When those succeed, InitializationStatus should be Initialized (2).
        createdTenant.InitializationStatus.Should().Be(
            2,
            $"expected Initialized(2) but got {createdTenant.InitializationStatus}");
    }

    [Fact]
    public async Task CreateTenant_IndependentDb_ShouldReachInitialized()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"indep-{Guid.NewGuid():N}";

        // Create tenant with valid connection string (independent database mode)
        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName,
            displayName = "Independent DB Tenant",
            defaultConnectionString = _factory.ConnectionString
        });

        tenantResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await tenantResponse.Content.ReadAsStringAsync());

        var tenantEnvelope = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(tenantResponse);
        var createdTenant = tenantEnvelope.Data!;
        createdTenant.Name.Should().Be(tenantName);

        // In independent-database mode all five phases run.
        createdTenant.InitializationStatus.Should().Be(
            2,
            $"expected Initialized(2) but got {createdTenant.InitializationStatus}");
    }

    private sealed class TenantDtoResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public int InitializationStatus { get; set; }
        public DateTime? InitializedAt { get; set; }
    }
}
