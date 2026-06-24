using System;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;

public sealed class PasswordGrantResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorDescription { get; init; }

    // Populated on success
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? TenantId { get; init; }
    public string? OrganizationId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public string[] Roles { get; init; } = Array.Empty<string>();

    public static PasswordGrantResult Fail(string description) =>
        new() { IsSuccess = false, ErrorDescription = description };

    public static PasswordGrantResult Success(
        Guid userId,
        string userName,
        string? email,
        string? tenantId,
        string? organizationId,
        bool isSuperAdmin,
        string[] roles) =>
        new()
        {
            IsSuccess = true,
            UserId = userId,
            UserName = userName,
            Email = email,
            TenantId = tenantId,
            OrganizationId = organizationId,
            IsSuperAdmin = isSuperAdmin,
            Roles = roles
        };
}
