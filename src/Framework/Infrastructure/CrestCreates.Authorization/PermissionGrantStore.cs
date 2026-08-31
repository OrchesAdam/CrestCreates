using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Authorization;

public class PermissionGrantStore : IPermissionGrantStore
{
    private readonly IPermissionGrantRepository _permissionGrantRepository;

    public PermissionGrantStore(
        IPermissionGrantRepository permissionGrantRepository)
    {
        _permissionGrantRepository = permissionGrantRepository;
    }

    public async Task<IReadOnlyList<PermissionGrantInfo>> GetGrantsAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return Array.Empty<PermissionGrantInfo>();
        }

        var grants = await _permissionGrantRepository.GetListByProviderAsync(providerType, providerKey.Trim(), cancellationToken);
        return grants.Select(MapToGrantInfo).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetGrantedPermissionsAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var grants = await GetGrantsAsync(providerType, providerKey, cancellationToken);

        return grants
            .Where(grant => MatchesScope(grant, tenantId))
            .Select(grant => grant.PermissionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permissionName => permissionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PermissionGrantInfo MapToGrantInfo(Domain.Permission.PermissionGrant grant)
    {
        return new PermissionGrantInfo
        {
            PermissionName = grant.PermissionName,
            ProviderType = grant.ProviderType,
            ProviderKey = grant.ProviderKey,
            Scope = grant.Scope,
            TenantId = grant.TenantId
        };
    }

    private static bool MatchesScope(PermissionGrantInfo grant, string? tenantId)
    {
        if (grant.Scope == PermissionGrantScope.Global)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(tenantId) &&
               string.Equals(grant.TenantId, tenantId, StringComparison.OrdinalIgnoreCase);
    }
}
