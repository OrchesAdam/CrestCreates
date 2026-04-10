using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Caching;

public class SettingCacheInvalidator
{
    private const string ItemCachePrefix = "Setting.Item";
    private const string ScopeCachePrefix = "Setting.Scope";

    private readonly ICrestCacheService _cacheService;
    private readonly SettingCacheKeyContributor _cacheKeyContributor;

    public SettingCacheInvalidator(
        ICrestCacheService cacheService,
        SettingCacheKeyContributor cacheKeyContributor)
    {
        _cacheService = cacheService;
        _cacheKeyContributor = cacheKeyContributor;
    }

    public async Task InvalidateAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var itemKey = _cacheKeyContributor.GetItemCacheKey(scope, providerKey, name, tenantId);
        var scopeKey = _cacheKeyContributor.GetScopeCacheKey(scope, providerKey, tenantId);

        await _cacheService.RemoveAsync(ItemCachePrefix, itemKey);
        await _cacheService.RemoveAsync(ScopeCachePrefix, scopeKey);
    }

    public Task InvalidateScopeAsync(
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var pattern = _cacheKeyContributor.GetScopePattern(scope, providerKey, tenantId);
        return _cacheService.RemoveByPatternAsync(pattern);
    }
}
