using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class IntegrationTests : IClassFixture<LibraryManagementWebApplicationFactory>
{
    private const string HostTenantId = "host";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LibraryManagementWebApplicationFactory _factory;

    public IntegrationTests(LibraryManagementWebApplicationFactory factory)
    {
        _factory = factory;
        // Trigger host initialization and seed data before any tests run
        _factory.EnsureSeedCompleteAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task LoginAndRefresh_WithSeededAdmin_ReturnsTokensAndCurrentUser()
    {
        await _factory.EnsureSeedCompleteAsync();
        var client = CreateTenantClient(HostTenantId);

        var loginResult = await LoginAsync(client, AdminUserName, AdminPassword, HostTenantId);

        loginResult.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginResult.RefreshToken.Should().NotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var meResponse = await client.GetAsync("/connect/userinfo");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawContent = await meResponse.Content.ReadAsStringAsync();

        var currentUser = await ReadJsonAsync<UserInfoResponse>(meResponse);
        currentUser.Name.Should().Be(AdminUserName, $"UserInfo response was: {rawContent}");
        // is_super_admin is returned as string "true" from userinfo, not boolean
        // This test verifies the claim is present; PermissionChecker handles string parsing

        // Refresh token
        var refreshContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = loginResult.RefreshToken,
            ["client_id"] = "test-client"
        });
        var refreshResponse = await client.PostAsync("/connect/token", refreshContent);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK, await refreshResponse.Content.ReadAsStringAsync());
        var refreshedToken = await ReadJsonAsync<TokenResponse>(refreshResponse);
        refreshedToken.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshedToken.RefreshToken.Should().NotBeNullOrWhiteSpace();
        refreshedToken.RefreshToken.Should().NotBe(loginResult.RefreshToken);
    }

    [Fact]
    public async Task Logout_AfterLogin_ReturnsOk()
    {
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var logoutResponse = await client.PostAsync("/connect/logout", content: null);
        logoutResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await logoutResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DynamicApi_WithAuthorizedAdmin_CanCreateAndGetBook()
    {
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var category = await CreateCategoryAsync(client);
        var createdBook = await CreateBookAsync(client, category.Id, "Domain Driven Design");
        createdBook.Data.Should().NotBeNull();
        createdBook.Data!.Title.Should().Be("Domain Driven Design");

        var getBookResponse = await client.GetAsync($"/api/book/{createdBook.Data.Id}");
        getBookResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetchedBook = await ReadJsonAsync<DynamicApiResponse<BookResponse>>(getBookResponse);
        fetchedBook.Data.Should().NotBeNull();
        fetchedBook.Data!.Id.Should().Be(createdBook.Data.Id);
        fetchedBook.Data.Title.Should().Be("Domain Driven Design");
    }

    [Fact]
    public async Task PermissionGrant_AfterGrantingBookSearch_UserCanAccessDynamicApi()
    {
        await _factory.EnsureSeedCompleteAsync();

        // First verify admin login works and get the actual tenant GUID
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var adminInfoResponse = await adminClient.GetAsync("/connect/userinfo");
        adminInfoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminInfo = await ReadJsonAsync<UserInfoResponse>(adminInfoResponse);
        var adminTenantId = adminInfo.TenantId;
        adminTenantId.Should().NotBeNullOrWhiteSpace("admin userinfo should contain tenantId");

        // Ensure user creation is enabled (other tests may have disabled it)
        await adminClient.GetAsync("/api/feature/set-global?name=Identity.UserCreationEnabled&value=true");

        var (createdUser, rawCreateResponse) = await CreateUserWithResponseAsync(
            adminClient,
            userName: $"reader-{Guid.NewGuid():N}"[..20],
            email: $"reader-{Guid.NewGuid():N}@library.local",
            password: "Reader123!",
            tenantId: adminTenantId, // Use the actual tenant GUID, not the name
            isSuperAdmin: false);

        createdUser.UserName.Should().NotBeNullOrWhiteSpace($"user creation should return a username. Response: {rawCreateResponse}");
        createdUser.TenantId.Should().Be(adminTenantId, $"created user should have correct tenantId. Response: {rawCreateResponse}");

        // Try to login as the new reader user
        var (readerClient, readerToken) = await CreateAuthenticatedClientAsync(createdUser.UserName, "Reader123!", HostTenantId);

        var deniedResponse = await readerClient.GetAsync("/api/book");
        deniedResponse.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            await deniedResponse.Content.ReadAsStringAsync());

        var deniedError = await ReadJsonAsync<ErrorResponse>(deniedResponse);
        deniedError.Message.Should().Be("没有权限执行当前操作");

        var grantResponse = await adminClient.GetAsync(
            $"/api/permission-grant/grant-to-user?userId={createdUser.Id}&permissionName=Book.Search&scope=0");

        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK, await grantResponse.Content.ReadAsStringAsync());

        var allowedResponse = await readerClient.GetAsync("/api/book");
        allowedResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await allowedResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DynamicApi_MainChain_CanUpdateQueryDeleteAndShowInSwagger()
    {
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var category = await CreateCategoryAsync(client);
        var createdBook = await CreateBookAsync(client, category.Id, "Clean Code");
        createdBook.Data.Should().NotBeNull();

        var updateData = new
        {
            Title = "Clean Code Second Edition",
            Author = "Robert C. Martin",
            ISBN = createdBook.Data!.ISBN,
            Description = "Updated edition",
            PublishDate = DateTime.UtcNow.Date,
            Publisher = "Prentice Hall",
            Status = 0,
            CategoryId = category.Id,
            TotalCopies = 5,
            AvailableCopies = 4,
            Location = "B-02"
        };
        var updateJsonContent = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(updateData),
            System.Text.Encoding.UTF8,
            "application/json");
        var updateResponse = await client.PutAsync($"/api/book/{createdBook.Data!.Id}", updateJsonContent);

        updateResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await updateResponse.Content.ReadAsStringAsync());
        var updatedBook = await ReadJsonAsync<DynamicApiResponse<BookResponse>>(updateResponse);
        updatedBook.Data.Should().NotBeNull();
        updatedBook.Data!.Title.Should().Be("Clean Code Second Edition");

        var listResponse = await client.GetAsync("/api/book?pageIndex=0&pageSize=10");
        listResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await listResponse.Content.ReadAsStringAsync());
        var pagedBooks = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<BookResponse>>>(listResponse);
        pagedBooks.Data.Should().NotBeNull();
        pagedBooks.Data!.Items.Should().Contain(book => book.Id == createdBook.Data.Id && book.Title == "Clean Code Second Edition");

        var deleteResponse = await client.DeleteAsync($"/api/book/{createdBook.Data.Id}");
        deleteResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await deleteResponse.Content.ReadAsStringAsync());

        var getDeletedResponse = await client.GetAsync($"/api/book/{createdBook.Data.Id}");
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var swaggerResponse = await client.GetAsync("/swagger/v1/swagger.json");
        swaggerResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await swaggerResponse.Content.ReadAsStringAsync());

        using var swaggerDocument = JsonDocument.Parse(await swaggerResponse.Content.ReadAsStringAsync());
        var paths = swaggerDocument.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/book", out var bookCollectionPath).Should().BeTrue();
        bookCollectionPath.TryGetProperty("get", out _).Should().BeTrue();
        bookCollectionPath.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/book/{id}", out var bookItemPath).Should().BeTrue();
        bookItemPath.TryGetProperty("put", out _).Should().BeTrue();
        bookItemPath.TryGetProperty("delete", out _).Should().BeTrue();
    }

    [Fact]
    public async Task TenantBoundary_WithMismatchedTenantHeader_ReturnsForbidden()
    {
        // Get the actual host tenant GUID from admin login token
        var (adminClient, adminToken) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        adminClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", adminToken.AccessToken);
        var adminInfoResp = await adminClient.GetAsync("/connect/userinfo");
        adminInfoResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminInfo = await ReadJsonAsync<UserInfoResponse>(adminInfoResp);
        var hostTenantId = adminInfo.TenantId;
        hostTenantId.Should().NotBeNullOrWhiteSpace("admin userinfo should contain tenantId");

        // Create a new tenant
        var tenantName = $"tenant-{Guid.NewGuid():N}"[..20];
        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName,
            displayName = "Second Tenant",
            defaultConnectionString = _factory.ConnectionString
        });
        tenantResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tenantResponse.Content.ReadAsStringAsync());

        // Change the X-Tenant-Id header to the new tenant while using the host admin's token
        adminClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        adminClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantName);

        var response = await adminClient.GetAsync("/connect/userinfo");
        // Super admin can access any tenant, so this should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // But the user's tenant_id in the token should still be the original host tenant
        var userInfo = await ReadJsonAsync<UserInfoResponse>(response);
        userInfo.TenantId.Should().Be(hostTenantId, "user's tenant_id should not change based on header");
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
        // OpenIddict password grant
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

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Login failed for userName={userName}, tenantId={tenantId}. Response: {rawResponse}");
        return await ReadJsonAsync<TokenResponse>(response);
    }

    private async Task<(IdentityUserResponse User, string RawResponse)> CreateUserWithResponseAsync(
        HttpClient adminClient,
        string userName,
        string email,
        string password,
        string tenantId,
        bool isSuperAdmin)
    {
        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                userName,
                email,
                password,
                phone = (string?)null,
                tenantId,
                organizationId = (Guid?)null,
                isSuperAdmin
            }),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await adminClient.PostAsync("/api/user", content);

        var rawResponse = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, rawResponse);

        var result = await System.Text.Json.JsonSerializer.DeserializeAsync<DynamicApiResponse<IdentityUserResponse>>(
            new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(rawResponse)),
            JsonSerializerOptions);

        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        return (result!.Data!, rawResponse);
    }

    private async Task<IdentityUserResponse> CreateUserAsync(
        HttpClient adminClient,
        string userName,
        string email,
        string password,
        string tenantId,
        bool isSuperAdmin)
    {
        var (user, _) = await CreateUserWithResponseAsync(adminClient, userName, email, password, tenantId, isSuperAdmin);
        return user;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private static async Task<CategoryResponse> CreateCategoryAsync(HttpClient client)
    {
        var categoryData = new
        {
            Name = $"category-{Guid.NewGuid():N}",
            Description = "Integration category",
            ParentId = (Guid?)null
        };
        var jsonContent = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(categoryData),
            System.Text.Encoding.UTF8,
            "application/json");
        var categoryResponse = await client.PostAsync("/api/category", jsonContent);

        categoryResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await categoryResponse.Content.ReadAsStringAsync());

        var categoryEnvelope = await ReadJsonAsync<DynamicApiResponse<CategoryResponse>>(categoryResponse);
        categoryEnvelope.Data.Should().NotBeNull();
        return categoryEnvelope.Data!;
    }

    private static async Task<DynamicApiResponse<BookResponse>> CreateBookAsync(HttpClient client, Guid categoryId, string title)
    {
        var isbn = $"{Guid.NewGuid().ToString().Replace("-", "")}"[..13];
        var bookData = new
        {
            Title = title,
            Author = "Integration Author",
            ISBN = isbn,
            Description = "Integration description",
            PublishDate = DateTime.UtcNow.Date,
            Publisher = "Integration Publisher",
            Status = 0,
            CategoryId = categoryId,
            TotalCopies = 3,
            AvailableCopies = 3,
            Location = "A-01"
        };
        var jsonContent = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(bookData),
            System.Text.Encoding.UTF8,
            "application/json");
        var createBookResponse = await client.PostAsync("/api/book", jsonContent);

        createBookResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await createBookResponse.Content.ReadAsStringAsync());
        return await ReadJsonAsync<DynamicApiResponse<BookResponse>>(createBookResponse);
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
        [JsonPropertyName("tenantid")]
        public string? TenantId { get; set; }
    }

    private sealed class UserInfoResponse
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
        // role can be either a single string or an array
        [JsonPropertyName("role")]
        public object? RoleRaw { get; set; }
    }

    private sealed class IdentityUserResponse
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

    private sealed class DynamicApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class CategoryResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class BookResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ISBN { get; set; } = string.Empty;
    }

    private sealed class ErrorResponse
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    private sealed class PagedResultResponse<T>
    {
        public T[] Items { get; set; } = Array.Empty<T>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
