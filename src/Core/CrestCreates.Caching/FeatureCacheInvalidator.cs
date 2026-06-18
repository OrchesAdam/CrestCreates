using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Caching;

public class FeatureCacheInvalidator
{
    private const string ItemCachePrefix = "Feature.Item";
    private const string ScopeCachePrefix = "Feature.Scope";

    private readonly ICrestCacheService _cacheService;
    private readonly FeatureCacheKeyContributor _cacheKeyContributor;

    public FeatureCacheInvalidator(
        ICrestCacheService cacheService,
        FeatureCacheKeyContributor cacheKeyContributor)
    {
        _cacheService = cacheService;
        _cacheKeyContributor = cacheKeyContributor;
    }

    public async Task InvalidateAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var itemKey = _cacheKeyContributor.GetItemCacheKey(scope, providerKey, name, tenantId);
        var scopeKey = _cacheKeyContributor.GetScopeCacheKey(scope, providerKey, tenantId);

        await _cacheService.RemoveAsync(ItemCachePrefix, itemKey);
        await _cacheService.RemoveAsync(ScopeCachePrefix, scopeKey);
    }

    public async Task InvalidateTenantAsync(
        string tenantId,
        string featureName,
        CancellationToken cancellationToken = default)
    {
        var itemKey = _cacheKeyContributor.GetItemCacheKey(FeatureScope.Tenant, tenantId, featureName, tenantId);
        var scopeKey = _cacheKeyContributor.GetScopeCacheKey(FeatureScope.Tenant, tenantId, tenantId);

        await _cacheService.RemoveAsync(ItemCachePrefix, itemKey);
        await _cacheService.RemoveAsync(ScopeCachePrefix, scopeKey);
    }

    public async Task InvalidateGlobalAsync(
        string featureName,
        CancellationToken cancellationToken = default)
    {
        var itemKey = _cacheKeyContributor.GetItemCacheKey(FeatureScope.Global, string.Empty, featureName, null);
        var scopeKey = _cacheKeyContributor.GetScopeCacheKey(FeatureScope.Global, string.Empty, null);

        await _cacheService.RemoveAsync(ItemCachePrefix, itemKey);
        await _cacheService.RemoveAsync(ScopeCachePrefix, scopeKey);
    }

    public Task InvalidateScopeAsync(
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var pattern = _cacheKeyContributor.GetScopePattern(scope, providerKey, tenantId);
        return _cacheService.RemoveByPatternAsync(pattern);
    }
}
