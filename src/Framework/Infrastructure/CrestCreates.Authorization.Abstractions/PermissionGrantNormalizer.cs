using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Authorization.Abstractions;

/// <summary>
/// Canonical grant shape used by every boundary. Scope checks, subject ownership and persistence
/// must all see the same normalized values so whitespace cannot select a different decision path.
/// </summary>
public static class PermissionGrantNormalizer
{
    public static PermissionGrantInfo Normalize(PermissionGrantInfo grant)
    {
        if (string.IsNullOrWhiteSpace(grant.PermissionName))
        {
            throw new ArgumentException("PermissionName is required.", nameof(grant));
        }

        if (string.IsNullOrWhiteSpace(grant.ProviderKey))
        {
            throw new ArgumentException("ProviderKey is required.", nameof(grant));
        }

        if (grant.Scope == PermissionGrantScope.Tenant && string.IsNullOrWhiteSpace(grant.TenantId))
        {
            throw new ArgumentException("TenantId is required when Scope is Tenant.", nameof(grant));
        }

        return new PermissionGrantInfo
        {
            PermissionName = grant.PermissionName.Trim(),
            ProviderType = grant.ProviderType,
            ProviderKey = grant.ProviderKey.Trim(),
            Scope = grant.Scope,
            TenantId = grant.Scope == PermissionGrantScope.Global ? null : grant.TenantId?.Trim()
        };
    }
}
