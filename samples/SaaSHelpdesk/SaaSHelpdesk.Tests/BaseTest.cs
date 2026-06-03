using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SaaSHelpdesk.Tests;

public abstract class BaseTest : IClassFixture<Fixtures.HelpdeskWebApplicationFactory>
{
    protected const string HostTenantId = "host";
    protected const string AdminUserName = "admin";
    protected const string AdminPassword = "Admin123!";

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected readonly Fixtures.HelpdeskWebApplicationFactory Factory;

    protected BaseTest(Fixtures.HelpdeskWebApplicationFactory factory)
    {
        Factory = factory;
        Factory.EnsureSeedCompleteAsync().GetAwaiter().GetResult();
    }

    // ── HTTP client creation ───────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient with the X-Tenant-Id header set.
    /// Disables auto-redirect to avoid losing tenant context on 302.
    /// </summary>
    protected HttpClient CreateTenantClient(string tenantId)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return client;
    }

    /// <summary>
    /// Creates an authenticated HttpClient and returns the login token.
    /// Login is performed against the given tenant and the Bearer token is
    /// preset on the returned client.
    /// </summary>
    protected async Task<(HttpClient Client, TokenResponse Token)> CreateAuthenticatedClientAsync(
        string userName,
        string password,
        string tenantId)
    {
        var client = CreateTenantClient(tenantId);
        var token = await LoginAsync(client, userName, password, tenantId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return (client, token);
    }

    /// <summary>
    /// Convenience overload that logs in with the default admin credentials.
    /// </summary>
    protected async Task<(HttpClient Client, TokenResponse Token)> CreateAuthenticatedAdminClientAsync(string tenantId = HostTenantId)
    {
        return await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, tenantId);
    }

    // ── Authentication ─────────────────────────────────────────────

    /// <summary>
    /// Performs an OpenIddict password-grant login and returns the token response.
    /// Uses "test-client" as the OAuth client_id (must be seeded by the factory).
    /// </summary>
    protected async Task<TokenResponse> LoginAsync(
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
            ["scope"] = "openid profile offline_access"
        });

        var response = await client.PostAsync("/connect/token", formContent);
        var rawResponse = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"Login failed for userName={userName}, tenantId={tenantId}. Response: {rawResponse}");

        return await ReadJsonAsync<TokenResponse>(response);
    }

    // ── HTTP verb helpers ──────────────────────────────────────────

    protected async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await client.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> PostAsync<T>(
        HttpClient client,
        string url,
        T body)
    {
        var json = SerializeJson(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        return await client.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string url,
        Dictionary<string, string> formData)
    {
        var content = new FormUrlEncodedContent(formData);
        return await client.PostAsync(url, content);
    }

    protected async Task<HttpResponseMessage> PutAsync<T>(
        HttpClient client,
        string url,
        T body)
    {
        var json = SerializeJson(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = content
        };
        return await client.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> DeleteAsync(
        HttpClient client,
        string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        return await client.SendAsync(request);
    }

    // ── JSON serialization / deserialization ───────────────────────

    protected static string SerializeJson<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    protected static T? DeserializeJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    protected static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        result.Should().NotBeNull();
        return result!;
    }

    protected static async Task<DynamicApiResponse<T>> ReadApiResponseAsync<T>(
        HttpResponseMessage response)
    {
        var result = await ReadJsonAsync<DynamicApiResponse<T>>(response);
        result.Data.Should().NotBeNull();
        return result;
    }

    // ── Response wrapper types ─────────────────────────────────────

    protected sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("tenantid")]
        public string? TenantId { get; set; }
    }

    protected sealed class UserInfoResponse
    {
        [JsonPropertyName("sub")]
        public string Sub { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("tenantid")]
        public string? TenantId { get; set; }

        [JsonPropertyName("is_super_admin")]
        public string IsSuperAdminRaw { get; set; } = string.Empty;

        public bool IsSuperAdmin => bool.TryParse(IsSuperAdminRaw, out var v) && v;

        [JsonPropertyName("role")]
        public object? RoleRaw { get; set; }
    }

    protected sealed class IdentityUserResponse
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("userName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("tenantId")]
        public string? TenantId { get; set; }
    }

    /// <summary>
    /// Standard envelope returned by Dynamic API endpoints.
    /// </summary>
    protected sealed class DynamicApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    protected sealed class ErrorResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public string? TraceId { get; set; }
        public int StatusCode { get; set; }
    }

    protected sealed class PagedResultResponse<T>
    {
        public T[] Items { get; set; } = Array.Empty<T>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }

    // ── User management helpers ────────────────────────────────────

    /// <summary>
    /// Creates a new user via the Dynamic API and returns the parsed user
    /// together with the raw JSON response.
    /// </summary>
    protected async Task<(IdentityUserResponse User, string RawResponse)> CreateUserAsync(
        HttpClient adminClient,
        string userName,
        string email,
        string password,
        string tenantId,
        bool isSuperAdmin = false,
        string? phone = null,
        string? role = null)
    {
        var payload = new
        {
            userName,
            email,
            password,
            phone,
            tenantId,
            organizationId = (Guid?)null,
            role,
            isSuperAdmin
        };

        var json = SerializeJson(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await adminClient.PostAsync("/api/user", content);

        var rawResponse = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"User creation failed. Response: {rawResponse}");

        var result = await ReadJsonAsync<DynamicApiResponse<IdentityUserResponse>>(response);
        result.Data.Should().NotBeNull();
        return (result.Data!, rawResponse);
    }
}
