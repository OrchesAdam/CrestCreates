using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Caching;

public class SettingCacheInvalidator
{
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
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetItemCacheKey(scope, providerKey, name, tenantId));
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetScopeCacheKey(scope, providerKey, tenantId));
    }

    public async Task InvalidateScopeAsync(
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetScopeCacheKey(scope, providerKey, tenantId));
        await _cacheService.RemoveByPatternAsync(
            _cacheKeyContributor.GetScopeItemPattern(scope, providerKey, tenantId));
    }
}
