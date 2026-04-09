using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Authorization;

public class PermissionGrantStore : IPermissionGrantStore
{
    private readonly IPermissionGrantRepository _permissionGrantRepository;
    private readonly PermissionGrantCacheService _permissionGrantCacheService;

    public PermissionGrantStore(
        IPermissionGrantRepository permissionGrantRepository,
        PermissionGrantCacheService permissionGrantCacheService)
    {
        _permissionGrantRepository = permissionGrantRepository;
        _permissionGrantCacheService = permissionGrantCacheService;
    }

    public Task<IReadOnlyList<PermissionGrantInfo>> GetGrantsAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return Task.FromResult<IReadOnlyList<PermissionGrantInfo>>(Array.Empty<PermissionGrantInfo>());
        }

        return _permissionGrantCacheService.GetOrAddAsync(
            providerType,
            providerKey.Trim(),
            async token =>
            {
                var grants = await _permissionGrantRepository.GetListByProviderAsync(providerType, providerKey.Trim(), token);
                return grants
                    .Select(MapToGrantInfo)
                    .ToArray();
            },
            cancellationToken);
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
