using System;
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
    }

    [Fact]
    public async Task LoginAndRefresh_WithSeededAdmin_ReturnsTokensAndCurrentUser()
    {
        var client = CreateTenantClient(HostTenantId);

        var loginResult = await LoginAsync(client, AdminUserName, AdminPassword, HostTenantId);

        loginResult.Token.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginResult.Token.RefreshToken.Should().NotBeNullOrWhiteSpace();
        loginResult.User.UserName.Should().Be(AdminUserName);
        loginResult.User.TenantId.Should().Be(HostTenantId);
        loginResult.User.IsSuperAdmin.Should().BeTrue();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token.AccessToken);

        var meResponse = await client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentUser = await ReadJsonAsync<UserInfoResponse>(meResponse);
        currentUser.UserName.Should().Be(AdminUserName);
        currentUser.TenantId.Should().Be(HostTenantId);
        currentUser.IsSuperAdmin.Should().BeTrue();

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = loginResult.Token.RefreshToken });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshedToken = await ReadJsonAsync<TokenResponse>(refreshResponse);
        refreshedToken.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshedToken.RefreshToken.Should().NotBeNullOrWhiteSpace();
        refreshedToken.RefreshToken.Should().NotBe(loginResult.Token.RefreshToken);
    }

    [Fact]
    public async Task Logout_AfterLogin_RevokesRefreshToken()
    {
        var (client, loginResult) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
        logoutResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await logoutResponse.Content.ReadAsStringAsync());

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = loginResult.Token.RefreshToken });

        refreshResponse.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            await refreshResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DynamicApi_WithAuthorizedAdmin_CanCreateAndGetBook()
    {
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var categoryName = $"category-{Guid.NewGuid():N}";
        var categoryResponse = await client.PostAsJsonAsync("/api/category", new
        {
            name = categoryName,
            description = "Integration category",
            parentId = (Guid?)null,
            parent = (object?)null,
            children = Array.Empty<object>(),
            books = Array.Empty<object>()
        });

        categoryResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await categoryResponse.Content.ReadAsStringAsync());
        var categoryEnvelope = await ReadJsonAsync<DynamicApiResponse<CategoryResponse>>(categoryResponse);
        categoryEnvelope.Data.Should().NotBeNull();

        var isbn = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}"[..13];
        var createBookResponse = await client.PostAsJsonAsync("/api/book", new
        {
            title = "Domain Driven Design",
            author = "Eric Evans",
            isbn,
            description = "A classic domain modeling reference.",
            publishDate = DateTime.UtcNow.Date,
            publisher = "Addison-Wesley",
            status = 0,
            categoryId = categoryEnvelope.Data!.Id,
            category = (object?)null,
            totalCopies = 3,
            availableCopies = 3,
            location = "A-01"
        });

        createBookResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await createBookResponse.Content.ReadAsStringAsync());
        var createdBook = await ReadJsonAsync<DynamicApiResponse<BookResponse>>(createBookResponse);
        createdBook.Data.Should().NotBeNull();
        createdBook.Data!.Title.Should().Be("Domain Driven Design");
        createdBook.Data.ISBN.Should().Be(isbn);

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
            $"/api/permissions/users/{createdUser.Id}/grants",
            new
            {
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
    public async Task TenantBoundary_WithMismatchedTenantHeader_ReturnsForbidden()
    {
        var (adminClient, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var tenantName = $"tenant-{Guid.NewGuid():N}"[..20];

        var tenantResponse = await adminClient.PostAsJsonAsync("/api/tenants", new
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

        var response = await memberClient.GetAsync("/api/auth/me");
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

    private async Task<(HttpClient Client, LoginResultResponse LoginResult)> CreateAuthenticatedClientAsync(
        string userName,
        string password,
        string tenantId)
    {
        var client = CreateTenantClient(tenantId);
        var loginResult = await LoginAsync(client, userName, password, tenantId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token.AccessToken);
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

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync<LoginResultResponse>(response);
    }

    private async Task<IdentityUserResponse> CreateUserAsync(
        HttpClient adminClient,
        string userName,
        string email,
        string password,
        string tenantId,
        bool isSuperAdmin)
    {
        var response = await adminClient.PostAsJsonAsync("/api/users", new
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
}
