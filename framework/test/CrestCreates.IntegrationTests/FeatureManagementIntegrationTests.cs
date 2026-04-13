using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class FeatureManagementIntegrationTests : IClassFixture<LibraryManagementWebApplicationFactory>
{
    private const string HostTenantId = "host";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LibraryManagementWebApplicationFactory _factory;

    public FeatureManagementIntegrationTests(LibraryManagementWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetFeatureDefinitions_ShouldReturnBuiltInFeatures()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var response = await hostClient.GetAsync("/api/feature-definition");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<FeatureDefinitionDto[]>>(response);
        envelope.Data.Should().NotBeNullOrEmpty();
        envelope.Data!.Any(f => f.Name == "Identity.UserCreationEnabled").Should().BeTrue();
        envelope.Data!.Any(f => f.Name == "FileManagement.Enabled").Should().BeTrue();
        envelope.Data!.Any(f => f.Name == "Storage.MaxFileCount").Should().BeTrue();
    }

    [Fact]
    public async Task SetGlobalFeature_ShouldPersistAndReturn()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var setResponse = await hostClient.PutAsJsonAsync(
            "/api/feature/set-global?name=Identity.UserCreationEnabled",
            new { value = "false" });

        setResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());

        var getResponse = await hostClient.GetAsync("/api/feature/global-values");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, await getResponse.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<FeatureValueDto[]>>(getResponse);
        var feature = envelope.Data!.SingleOrDefault(f => f.Name == "Identity.UserCreationEnabled");
        feature.Should().NotBeNull();
        feature!.Value.Should().Be("false");
    }

    [Fact]
    public async Task SetTenantFeature_ShouldOverrideGlobal()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        await hostClient.PutAsJsonAsync(
            "/api/feature/set-global?name=Identity.UserCreationEnabled",
            new { value = "false" });

        var tenantId = await CreateTenantAndReturnIdAsync();
        var (tenantClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);

        var setResponse = await tenantClient.PutAsJsonAsync(
            "/api/feature/set-tenant?name=Identity.UserCreationEnabled",
            new { tenantId, value = "true" });

        setResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());

        var getResponse = await tenantClient.GetAsync("/api/feature/current-tenant-values");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, await getResponse.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<FeatureValueDto[]>>(getResponse);
        var feature = envelope.Data!.SingleOrDefault(f => f.Name == "Identity.UserCreationEnabled");
        feature.Should().NotBeNull();
        feature!.Value.Should().Be("true");
        feature.Scope.Should().Be(FeatureScope.Tenant);
    }

    [Fact]
    public async Task RemoveTenantFeature_ShouldFallbackToGlobal()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        await hostClient.PutAsJsonAsync(
            "/api/feature/set-global?name=Identity.UserCreationEnabled",
            new { value = "false" });

        var tenantId = await CreateTenantAndReturnIdAsync();
        var (tenantClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);

        await tenantClient.PutAsJsonAsync(
            "/api/feature/set-tenant?name=Identity.UserCreationEnabled",
            new { tenantId, value = "true" });

        var deleteResponse = await tenantClient.DeleteAsync(
            $"/api/feature/delete-tenant?name=Identity.UserCreationEnabled&tenantId={tenantId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK, await deleteResponse.Content.ReadAsStringAsync());

        var getResponse = await tenantClient.GetAsync("/api/feature/current-tenant-values");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, await getResponse.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<FeatureValueDto[]>>(getResponse);
        var feature = envelope.Data!.SingleOrDefault(f => f.Name == "Identity.UserCreationEnabled");
        feature.Should().NotBeNull();
        feature!.Value.Should().Be("false");
        feature.Scope.Should().Be(FeatureScope.Global);
    }

    [Fact]
    public async Task IsEnabled_ShouldReturnCorrectBoolValue()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        await hostClient.PutAsJsonAsync(
            "/api/feature/set-global?name=Identity.UserCreationEnabled",
            new { value = "true" });

        var isEnabledResponse = await hostClient.GetAsync("/api/feature/is-enabled?name=Identity.UserCreationEnabled");
        isEnabledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await isEnabledResponse.Content.ReadAsStringAsync());

        var isEnabledEnvelope = await ReadJsonAsync<DynamicApiResponse<bool>>(isEnabledResponse);
        isEnabledEnvelope.Data.Should().BeTrue();
    }

    [Fact]
    public async Task IsTenantEnabled_ShouldUseExplicitTenantId_NotCurrentTenant()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var tenant1Id = await CreateTenantAndReturnIdAsync();
        var tenant2Id = await CreateTenantAndReturnIdAsync();

        await hostClient.PutAsJsonAsync(
            "/api/feature/set-global?name=Identity.UserCreationEnabled",
            new { value = "false" });

        await hostClient.PutAsJsonAsync(
            "/api/feature/set-tenant?name=Identity.UserCreationEnabled",
            new { tenantId = tenant1Id, value = "true" });

        var tenant1EnabledResponse = await hostClient.GetAsync($"/api/feature/is-tenant-enabled?tenantId={tenant1Id}&featureName=Identity.UserCreationEnabled");
        tenant1EnabledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tenant1EnabledResponse.Content.ReadAsStringAsync());
        var tenant1Envelope = await ReadJsonAsync<DynamicApiResponse<bool>>(tenant1EnabledResponse);
        tenant1Envelope.Data.Should().BeTrue();

        var tenant2EnabledResponse = await hostClient.GetAsync($"/api/feature/is-tenant-enabled?tenantId={tenant2Id}&featureName=Identity.UserCreationEnabled");
        tenant2EnabledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tenant2EnabledResponse.Content.ReadAsStringAsync());
        var tenant2Envelope = await ReadJsonAsync<DynamicApiResponse<bool>>(tenant2EnabledResponse);
        tenant2Envelope.Data.Should().BeFalse();
    }

    [Fact]
    public async Task FeatureControlsRealCapability_TenantOverridesGlobal()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        await hostClient.PutAsJsonAsync(
            "/api/feature/set-global?name=Identity.UserCreationEnabled",
            new { value = "false" });

        var tenantId = await CreateTenantAndReturnIdAsync();
        var (tenantClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);

        await tenantClient.PutAsJsonAsync(
            "/api/feature/set-tenant?name=Identity.UserCreationEnabled",
            new { tenantId, value = "true" });

        var globalEnabledResponse = await hostClient.GetAsync("/api/feature/is-enabled?name=Identity.UserCreationEnabled");
        var globalEnvelope = await ReadJsonAsync<DynamicApiResponse<bool>>(globalEnabledResponse);
        globalEnvelope.Data.Should().BeFalse();

        var tenantEnabledResponse = await tenantClient.GetAsync($"/api/feature/is-tenant-enabled?tenantId={tenantId}&featureName=Identity.UserCreationEnabled");
        var tenantEnvelope = await ReadJsonAsync<DynamicApiResponse<bool>>(tenantEnabledResponse);
        tenantEnvelope.Data.Should().BeTrue();

        var testUserName = $"testuser-{Guid.NewGuid():N}"[..20];
        var globalCreateResponse = await hostClient.PostAsJsonAsync("/api/identity/user", new
        {
            userName = testUserName,
            email = $"{testUserName}@test.com",
            password = "Test123!",
            tenantId = HostTenantId
        });
        globalCreateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, await globalCreateResponse.Content.ReadAsStringAsync());

        var tenantCreateResponse = await tenantClient.PostAsJsonAsync("/api/identity/user", new
        {
            userName = testUserName,
            email = $"{testUserName}@test.com",
            password = "Test123!",
            tenantId = tenantId
        });
        tenantCreateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tenantCreateResponse.Content.ReadAsStringAsync());
    }

    private async Task<string> CreateTenantAndReturnIdAsync()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"feature-tenant-{Guid.NewGuid():N}"[..20];

        var response = await adminClient.PostAsJsonAsync("/api/tenants", new
        {
            name = tenantName,
            displayName = $"Tenant {tenantName}",
            defaultConnectionString = _factory.ConnectionString
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var tenant = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(response);
        return tenant.Data!.Id.ToString();
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
        var loginResult = await LoginAsync(client, userName, password, tenantId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
        return (client, loginResult);
    }

    private async Task<TokenResponse> LoginAsync(
        HttpClient client,
        string userName,
        string password,
        string tenantId)
    {
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = userName,
            ["password"] = password,
            ["scope"] = "openid profile email"
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

    private sealed class TokenResponse Token { get; set; } = new();
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
    }

    private sealed class FeatureDefinitionDto
    {
        public string Name { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? DefaultValue { get; set; }
        public FeatureValueType ValueType { get; set; }
        public bool IsVisible { get; set; }
        public bool IsEditable { get; set; }
        public FeatureScope Scopes { get; set; }
    }

    private sealed class FeatureValueDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Value { get; set; }
        public FeatureScope? Scope { get; set; }
        public string? ProviderKey { get; set; }
        public string? TenantId { get; set; }
    }
}
