using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class SettingManagementIntegrationTests : IClassFixture<LibraryManagementWebApplicationFactory>
{
    private const string HostTenantId = "host";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LibraryManagementWebApplicationFactory _factory;

    public SettingManagementIntegrationTests(LibraryManagementWebApplicationFactory factory)
    {
        _factory = factory;
        // Trigger host initialization and seed data before any tests run
        _factory.EnsureSeedCompleteAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task TenantSetting_UpdatedThroughDynamicApi_ShouldAffectCurrentValues()
    {
        var tenantId = await CreateTenantAndReturnIdAsync();
        var (tenantClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);

        var updateResponse = await tenantClient.PutAsJsonAsync(
            "/api/setting/update-current-tenant?name=App.DisplayName",
            new { value = "Tenant Display" });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await updateResponse.Content.ReadAsStringAsync());

        var currentValuesResponse = await tenantClient.GetAsync("/api/setting/current-values");
        currentValuesResponse.StatusCode.Should().Be(HttpStatusCode.OK, await currentValuesResponse.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<SettingValueResponse[]>>(currentValuesResponse);
        var displayNameSetting = envelope.Data!.Single(setting => setting.Name == "App.DisplayName");
        displayNameSetting.Value.Should().Be("Tenant Display");
        displayNameSetting.Scope.Should().Be(2);
    }

    [Fact]
    public async Task EncryptedSetting_UpdatedThroughDynamicApi_ShouldNotLeakPlaintext()
    {
        var tenantId = await CreateTenantAndReturnIdAsync();
        var (tenantClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);

        var updateResponse = await tenantClient.PutAsJsonAsync(
            "/api/setting/update-current-tenant?name=Storage.SecretKey",
            new { value = "secret-value-123" });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await updateResponse.Content.ReadAsStringAsync());

        var currentValuesResponse = await tenantClient.GetAsync("/api/setting/current-values");
        currentValuesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await ReadJsonAsync<DynamicApiResponse<SettingValueResponse[]>>(currentValuesResponse);
        var secretSetting = envelope.Data!.Single(setting => setting.Name == "Storage.SecretKey");
        secretSetting.Value.Should().BeNull();
        secretSetting.MaskedValue.Should().NotBe("secret-value-123");
        secretSetting.MaskedValue.Should().Contain("***");
    }

    [Fact]
    public async Task DeleteOverride_ShouldFallbackFromUserToTenantToGlobal()
    {
        var (hostClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var globalUpdateResponse = await hostClient.PutAsJsonAsync(
            "/api/setting/update-global?name=App.DisplayName",
            new { value = "Global Display" });

        globalUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await globalUpdateResponse.Content.ReadAsStringAsync());

        var tenantId = await CreateTenantAndReturnIdAsync();
        var (tenantClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);

        var tenantUpdateResponse = await tenantClient.PutAsJsonAsync(
            "/api/setting/update-current-tenant?name=App.DisplayName",
            new { value = "Tenant Display" });
        tenantUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tenantUpdateResponse.Content.ReadAsStringAsync());

        var userUpdateResponse = await tenantClient.PutAsJsonAsync(
            "/api/setting/update-current-user?name=App.DisplayName",
            new { value = "User Display" });
        userUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await userUpdateResponse.Content.ReadAsStringAsync());

        (await GetCurrentSettingAsync(tenantClient, "App.DisplayName")).Value.Should().Be("User Display");

        var deleteUserResponse = await tenantClient.DeleteAsync("/api/setting/delete-current-user?name=App.DisplayName");
        deleteUserResponse.StatusCode.Should().Be(HttpStatusCode.OK, await deleteUserResponse.Content.ReadAsStringAsync());
        (await GetCurrentSettingAsync(tenantClient, "App.DisplayName")).Value.Should().Be("Tenant Display");

        var deleteTenantResponse = await tenantClient.DeleteAsync("/api/setting/delete-current-tenant?name=App.DisplayName");
        deleteTenantResponse.StatusCode.Should().Be(HttpStatusCode.OK, await deleteTenantResponse.Content.ReadAsStringAsync());
        (await GetCurrentSettingAsync(tenantClient, "App.DisplayName")).Value.Should().Be("Global Display");
    }

    private async Task<string> CreateTenantAndReturnIdAsync()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"setting-tenant-{Guid.NewGuid():N}"[..20];

        var response = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName,
            displayName = $"Tenant {tenantName}",
            defaultConnectionString = _factory.ConnectionString
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var tenant = await ReadJsonAsync<DynamicApiResponse<TenantDtoResponse>>(response);
        return tenant.Data!.Id.ToString();
    }

    private async Task<SettingValueResponse> GetCurrentSettingAsync(HttpClient client, string name)
    {
        var response = await client.GetAsync("/api/setting/current-values");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<SettingValueResponse[]>>(response);
        return envelope.Data!.Single(setting => setting.Name == name);
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
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

    private sealed class SettingValueResponse
    {
        public string Name { get; set; } = string.Empty;
        public int? Scope { get; set; }
        public string? Value { get; set; }
        public string MaskedValue { get; set; } = string.Empty;
    }
}
