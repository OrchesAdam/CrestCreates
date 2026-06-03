namespace SaaSHelpdesk.Tests.Helpers;

/// <summary>
/// Standard CrestCreates Dynamic API envelope response.
/// </summary>
public sealed class DynamicApiResponse<T>
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

/// <summary>
/// Paged result wrapper returned by Dynamic API list endpoints.
/// </summary>
public sealed class PagedResultResponse<T>
{
    public T[] Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// OAuth2 / OpenIddict token response from /connect/token.
/// </summary>
public sealed class TokenResponse
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

/// <summary>
/// OpenIddict userinfo response from /connect/userinfo.
/// </summary>
public sealed class UserInfoResponse
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

/// <summary>
/// Standard error response body.
/// </summary>
public sealed class ErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? TraceId { get; set; }
    public int StatusCode { get; set; }
}

/// <summary>
/// Minimal identity user response (returned by /api/user endpoints).
/// </summary>
public sealed class IdentityUserResponse
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
/// Simplified Customer response for test assertions.
/// </summary>
public sealed class CustomerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Simplified Ticket response for test assertions.
/// </summary>
public sealed class TicketDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Status { get; set; }
    public int Priority { get; set; }
    public int Type { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? CategoryId { get; set; }
    public DateTime? DueDate { get; set; }
    public string? ConcurrencyStamp { get; set; }
}

/// <summary>
/// Simplified Category response for test assertions.
/// </summary>
public sealed class CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}
