using SaaSHelpdesk.Tests.Helpers;

namespace SaaSHelpdesk.Tests.Fixtures;

/// <summary>
/// Shared JSON serializer options for integration tests.
/// </summary>
public static class TestDefaults
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public const string HostTenantId = "host";
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Admin123!";
}

/// <summary>
/// Authentication helper for login, token refresh, and client configuration.
/// </summary>
public static class TestAuthHelper
{
    private static readonly JsonSerializerOptions JsonOptions = TestDefaults.JsonOptions;

    /// <summary>
    /// Configures an existing HttpClient with the X-Tenant-Id header.
    /// </summary>
    public static void SetTenantHeader(HttpClient client, string tenantId)
    {
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
    }

    /// <summary>
    /// Performs OpenIddict password grant login and returns the token response.
    /// </summary>
    public static async Task<TokenResponse> LoginAsync(
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

        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<TokenResponse>(response);
    }

    /// <summary>
    /// Logs in and sets the bearer token on the client.
    /// </summary>
    public static async Task<TokenResponse> AuthenticateClientAsync(
        HttpClient client,
        string userName,
        string password,
        string tenantId)
    {
        var token = await LoginAsync(client, userName, password, tenantId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return token;
    }

    /// <summary>
    /// Reads and deserializes a JSON HTTP response.
    /// </summary>
    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        result.Should().NotBeNull();
        return result!;
    }
}
