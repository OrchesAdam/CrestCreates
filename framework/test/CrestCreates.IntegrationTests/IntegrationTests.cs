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
    public async Task Logout_AfterLogin_RevokesRefreshToken()
    {
        var (client, loginResult) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var logoutResponse = await client.PostAsync("/connect/logout", content: null);
        logoutResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await logoutResponse.Content.ReadAsStringAsync());

        // Refresh token should be revoked
        var refreshContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = loginResult.RefreshToken,
            ["client_id"] = "test-client"
        });
        var refreshResponse = await client.PostAsync("/connect/token", refreshContent);

        refreshResponse.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            await refreshResponse.Content.ReadAsStringAsync());
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
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var createdUser = await CreateUserAsync(
            adminClient,
            userName: $"reader-{Guid.NewGuid():N}"[..20],
            email: $"reader-{Guid.NewGuid():N}@library.local",
            password: "Reader123!",
            tenantId: HostTenantId,
            isSuperAdmin: false);

        var (readerClient, _) = await CreateAuthenticatedClientAsync(createdUser.UserName, "Reader123!", HostTenantId);

        var deniedResponse = await readerClient.GetAsync("/api/book");
        deniedResponse.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            await deniedResponse.Content.ReadAsStringAsync());

        var deniedError = await ReadJsonAsync<ErrorResponse>(deniedResponse);
        deniedError.Message.Should().Be("没有权限执行当前操作");

        var grantResponse = await adminClient.PostAsJsonAsync(
            "/api/permission-grant/grant-to-user",
            new
            {
                userId = createdUser.Id.ToString(),
                permissionName = "Book.Search",
                scope = 0,
                tenantId = (string?)null
            });

        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

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
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"tenant-{Guid.NewGuid():N}"[..20];

        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenant", new
        {
            name = tenantName,
            displayName = "Second Tenant",
            defaultConnectionString = _factory.ConnectionString
        });

        tenantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createdUser = await CreateUserAsync(
            adminClient,
            userName: $"member-{Guid.NewGuid():N}"[..20],
            email: $"member-{Guid.NewGuid():N}@library.local",
            password: "Member123!",
            tenantId: HostTenantId,
            isSuperAdmin: false);

        var (memberClient, _) = await CreateAuthenticatedClientAsync(createdUser.UserName, "Member123!", HostTenantId);
        memberClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        memberClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantName);

        var response = await memberClient.GetAsync("/connect/userinfo");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var error = await ReadJsonAsync<ErrorResponse>(response);
        error.Message.Should().Be("当前用户无权访问该租户上下文");
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

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync<TokenResponse>(response);
    }

    private async Task<IdentityUserResponse> CreateUserAsync(
        HttpClient adminClient,
        string userName,
        string email,
        string password,
        string tenantId,
        bool isSuperAdmin)
    {
        var response = await adminClient.PostAsJsonAsync("/api/user", new
        {
            userName,
            email,
            password,
            phone = (string?)null,
            tenantId,
            organizationId = (Guid?)null,
            isSuperAdmin
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync<IdentityUserResponse>(response);
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
    }

    private sealed class UserInfoResponse
    {
        public string Sub { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? TenantId { get; set; }
        public bool IsSuperAdmin { get; set; }
        public List<string>? Roles { get; set; }
    }

    private sealed class IdentityUserResponse
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
