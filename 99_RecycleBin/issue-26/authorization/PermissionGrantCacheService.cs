using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Authorization;

public sealed class PermissionGrantCacheService
{
    private const string CachePrefix = "Authorization.PermissionGrant";

    private readonly ICrestCacheService _cacheService;
    private readonly PermissionGrantCacheOptions _options;

    public PermissionGrantCacheService(
        ICrestCacheService cacheService,
        PermissionGrantCacheOptions options)
    {
        _cacheService = cacheService;
        _options = options;
    }

    public async Task<IReadOnlyList<PermissionGrantInfo>> GetOrAddAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        Func<CancellationToken, Task<IReadOnlyList<PermissionGrantInfo>>> factory,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CreateCacheKey(providerType, providerKey);
        var cachedGrants = await _cacheService.GetAsync<List<PermissionGrantInfo>>(CachePrefix, cacheKey);
        if (cachedGrants != null)
        {
            return cachedGrants;
        }

        var grants = await factory(cancellationToken);
        var grantList = grants.ToList();

        await _cacheService.SetAsync(CachePrefix, grantList, _options.Expiration, cacheKey);

        return grantList;
    }

    public Task RemoveAsync(PermissionGrantProviderType providerType, string providerKey)
    {
        var cacheKey = CreateCacheKey(providerType, providerKey);
        return _cacheService.RemoveAsync(CachePrefix, cacheKey);
    }

    private static string CreateCacheKey(PermissionGrantProviderType providerType, string providerKey)
    {
        return $"{providerType}:{providerKey}";
    }
}
