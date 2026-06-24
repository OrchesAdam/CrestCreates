using System;
using System.Collections.Generic;

namespace CrestCreates.Infrastructure.Authorization;

/// <summary>
/// 用户身份信息上下文，用于构建 claims
/// </summary>
public sealed class IdentityClaimsContext
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? TenantId { get; init; }
    public Guid? OrganizationId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Permissions { get; init; } = Array.Empty<string>();
}
