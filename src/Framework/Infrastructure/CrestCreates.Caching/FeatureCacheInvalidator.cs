using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Caching;

public class FeatureCacheInvalidator
{
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
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetItemCacheKey(scope, providerKey, name, tenantId));
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetScopeCacheKey(scope, providerKey, tenantId));
    }

    public async Task InvalidateTenantAsync(
        string tenantId,
        string featureName,
        CancellationToken cancellationToken = default)
    {
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetItemCacheKey(FeatureScope.Tenant, tenantId, featureName, tenantId));
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetScopeCacheKey(FeatureScope.Tenant, tenantId, tenantId));
    }

    public async Task InvalidateGlobalAsync(
        string featureName,
        CancellationToken cancellationToken = default)
    {
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetItemCacheKey(FeatureScope.Global, string.Empty, featureName, null));
        await _cacheService.RemoveAsync(
            _cacheKeyContributor.GetScopeCacheKey(FeatureScope.Global, string.Empty, null));
    }

    public async Task InvalidateScopeAsync(
        FeatureScope scope,
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
