using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class FeatureManagementIntegrationTests : IClassFixture<ErpManagementWebApplicationFactory>
{
    private const string HostTenantId = "host";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Platform JSON contracts serialize enums as string names (e.g. "Tenant")
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ErpManagementWebApplicationFactory _factory;

    public FeatureManagementIntegrationTests(ErpManagementWebApplicationFactory factory)
    {
        _factory = factory;
        _ = _factory.CreateClient();
        _factory.EnsureSeedCompleteAsync().GetAwaiter().GetResult();
    }

    // Use FileManagement.Enabled for feature value tests to avoid polluting Identity.UserCreationEnabled
    // which is used by other integration tests for actual user creation
    private const string TestFeatureName = "FileManagement.Enabled";
    private const string TestFeatureGlobalValue = "true";
    private const string TestFeatureTenantValue = "false";

    [Fact]
    public async Task GetFeatureDefinitions_ShouldReturnBuiltInFeatures()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var response = await hostClient.GetAsync("/api/feature-definitions");
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

        var setResponse = await hostClient.PutAsync(
            $"/api/features/global/{TestFeatureName}?value={TestFeatureGlobalValue}", EmptyJsonContent());

        setResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());

        var getResponse = await hostClient.GetAsync("/api/features/global");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, await getResponse.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<FeatureValueDto[]>>(getResponse);
        var feature = envelope.Data!.SingleOrDefault(f => f.Name == TestFeatureName);
        feature.Should().NotBeNull();
        feature!.Value.Should().Be(TestFeatureGlobalValue);
    }

    [Fact]
    public async Task SetTenantFeature_ShouldOverrideGlobal()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        await hostClient.PutAsync(
            $"/api/features/global/{TestFeatureName}?value={TestFeatureGlobalValue}", EmptyJsonContent());

        var tenantId = await CreateTenantAndReturnIdAsync();

        var setResponse = await hostClient.PutAsync(
            $"/api/features/tenant/{tenantId}/{TestFeatureName}?value={TestFeatureTenantValue}", EmptyJsonContent());

        setResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());

        var (tenantClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);
        var getResponse = await tenantClient.GetAsync("/api/features/current-tenant");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, await getResponse.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<FeatureValueDto[]>>(getResponse);
        var feature = envelope.Data!.SingleOrDefault(f => f.Name == TestFeatureName);
        feature.Should().NotBeNull();
        feature!.Value.Should().Be(TestFeatureTenantValue);
        feature.Scope.Should().Be(FeatureScope.Tenant);
    }

    [Fact]
    public async Task RemoveTenantFeature_ShouldFallbackToGlobal()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // Set global value explicitly and verify it was set
        await hostClient.PutAsync(
            $"/api/features/global/{TestFeatureName}?value={TestFeatureGlobalValue}", EmptyJsonContent());

        var verifyGlobalResponse = await hostClient.GetAsync("/api/features/global");
        verifyGlobalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var globalEnvelope = await ReadJsonAsync<DynamicApiResponse<FeatureValueDto[]>>(verifyGlobalResponse);
        var globalFeature = globalEnvelope.Data!.SingleOrDefault(f => f.Name == TestFeatureName);
        globalFeature.Should().NotBeNull();
        globalFeature!.Value.Should().Be(TestFeatureGlobalValue, "global value should be set before testing fallback");

        var tenantId = await CreateTenantAndReturnIdAsync();

        await hostClient.PutAsync(
            $"/api/features/tenant/{tenantId}/{TestFeatureName}?value={TestFeatureTenantValue}", EmptyJsonContent());

        var deleteResponse = await hostClient.DeleteAsync(
            $"/api/features/tenant/{tenantId}/{TestFeatureName}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK, await deleteResponse.Content.ReadAsStringAsync());

        // After removing tenant override, the feature should fallback to global
        // Use the tenant enabled check to verify the fallback
        var verifyResponse = await hostClient.GetAsync($"/api/features/tenant/{tenantId}/enabled/{TestFeatureName}");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK, await verifyResponse.Content.ReadAsStringAsync());
        var verifyEnvelope = await ReadJsonAsync<DynamicApiResponse<bool>>(verifyResponse);
        verifyEnvelope.Data.Should().BeTrue("should fallback to global value (true) after tenant override is removed");
    }

    [Fact]
    public async Task IsEnabled_ShouldReturnCorrectBoolValue()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        await hostClient.PutAsync(
            $"/api/features/global/{TestFeatureName}?value={TestFeatureGlobalValue}", EmptyJsonContent());

        var isEnabledResponse = await hostClient.GetAsync($"/api/features/enabled/{TestFeatureName}");
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

        await hostClient.PutAsync(
            $"/api/features/global/{TestFeatureName}?value={TestFeatureGlobalValue}", EmptyJsonContent());

        await hostClient.PutAsync(
            $"/api/features/tenant/{tenant1Id}/{TestFeatureName}?value={TestFeatureTenantValue}", EmptyJsonContent());

        var tenant1EnabledResponse = await hostClient.GetAsync($"/api/features/tenant/{tenant1Id}/enabled/{TestFeatureName}");
        tenant1EnabledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tenant1EnabledResponse.Content.ReadAsStringAsync());
        var tenant1Envelope = await ReadJsonAsync<DynamicApiResponse<bool>>(tenant1EnabledResponse);
        tenant1Envelope.Data.Should().BeFalse();

        var tenant2EnabledResponse = await hostClient.GetAsync($"/api/features/tenant/{tenant2Id}/enabled/{TestFeatureName}");
        tenant2EnabledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tenant2EnabledResponse.Content.ReadAsStringAsync());
        var tenant2Envelope = await ReadJsonAsync<DynamicApiResponse<bool>>(tenant2EnabledResponse);
        tenant2Envelope.Data.Should().BeTrue();
    }

    [Fact]
    public async Task FeatureControlsRealCapability_TenantOverridesGlobal()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // Use FileManagement.Enabled instead of Identity.UserCreationEnabled
        // to avoid polluting shared state that other tests depend on
        var featureName = "FileManagement.Enabled";

        // Disable globally
        await hostClient.PutAsync(
            $"/api/features/global/{featureName}?value=false", EmptyJsonContent());

        var tenantId = await CreateTenantAndReturnIdAsync();

        // Enable for tenant
        await hostClient.PutAsync(
            $"/api/features/tenant/{tenantId}/{featureName}?value=true", EmptyJsonContent());

        // Verify global resolution returns false
        var globalEnabledResponse = await hostClient.GetAsync($"/api/features/enabled/{featureName}");
        globalEnabledResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var globalEnvelope = await ReadJsonAsync<DynamicApiResponse<bool>>(globalEnabledResponse);
        globalEnvelope.Data.Should().BeFalse("global value should be false");

        // Verify tenant resolution returns true
        var tenantEnabledResponse = await hostClient.GetAsync($"/api/features/tenant/{tenantId}/enabled/{featureName}");
        tenantEnabledResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenantEnvelope = await ReadJsonAsync<DynamicApiResponse<bool>>(tenantEnabledResponse);
        tenantEnvelope.Data.Should().BeTrue("tenant value should be true");
    }

    [Fact]
    public async Task FeatureDynamicApi_ShouldWorkOnGeneratedPath()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var definitionsResponse = await hostClient.GetAsync("/api/feature-definitions/groups");
        definitionsResponse.StatusCode.Should().Be(HttpStatusCode.OK, await definitionsResponse.Content.ReadAsStringAsync());

        var setResponse = await hostClient.PutAsync(
            "/api/features/global/Identity.UserCreationEnabled?value=false", EmptyJsonContent());
        setResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());

        var enabledResponse = await hostClient.GetAsync("/api/features/enabled/Identity.UserCreationEnabled");
        enabledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await enabledResponse.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<bool>>(enabledResponse);
        envelope.Data.Should().BeFalse();
    }

    [Fact]
    public async Task TenantUser_ShouldNotManageOtherTenantFeature()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var tenantAId = await CreateTenantAndReturnIdAsync();
        var tenantBId = await CreateTenantAndReturnIdAsync();
        var (tenantAClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantAId);

        var response = await tenantAClient.PutAsync(
            $"/api/features/tenant/{tenantBId}/Identity.UserCreationEnabled?value=false", EmptyJsonContent());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FeatureChange_ShouldWriteAuditLog()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var response = await hostClient.PutAsync(
            "/api/features/global/Identity.UserCreationEnabled?value=true", EmptyJsonContent());
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // Give the audit middleware time to flush the audit log
        await Task.Delay(500);

        // 正式主链 = Accountability HTTP terminal observer → IAuditSink（夹具替换为内存 sink）。
        // 旧 AuditContext → AuditLogs 表链路已退出 UseCrestWeb，DB 查询看不到实时审计；
        // 与 AuditLogIntegrationTests 的 sink 断言先例一致。
        _factory.AuditSink.GetRecords().Should().Contain(record =>
            record.Action.Name.StartsWith("PUT ") &&
            record.Action.Name.Contains("features/global") &&
            record.Outcome.Status == "succeeded");
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
        var envelope = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(response);
        var tenant = envelope.Data!;
        return tenant.Id.ToString();
    }

    private static StringContent EmptyJsonContent() => new("", System.Text.Encoding.UTF8, "application/json");

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

        if (tenantId == HostTenantId)
        {
            client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        }

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
