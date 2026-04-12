using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

public class FeatureStore : IFeatureStore
{
    private const string ItemCachePrefix = "Feature.Item";
    private const string ScopeCachePrefix = "Feature.Scope";

    private readonly IFeatureRepository _featureRepository;
    private readonly ICrestCacheService _cacheService;
    private readonly FeatureCacheKeyContributor _cacheKeyContributor;

    public FeatureStore(
        IFeatureRepository featureRepository,
        ICrestCacheService cacheService,
        FeatureCacheKeyContributor cacheKeyContributor)
    {
        _featureRepository = featureRepository;
        _cacheService = cacheService;
        _cacheKeyContributor = cacheKeyContributor;
    }

    public async Task<FeatureValueEntry?> GetOrNullAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);
        var cacheKey = _cacheKeyContributor.GetItemCacheKey(scope, normalizedProviderKey, name, normalizedTenantId);
        var cached = await _cacheService.GetAsync<FeatureValueEntry?>(ItemCachePrefix, cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var feature = await _featureRepository.FindAsync(
            name.Trim(),
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        if (feature is null)
        {
            return null;
        }

        var entry = MapToEntry(feature);
        await _cacheService.SetAsync(ItemCachePrefix, entry, cacheKey);
        return entry;
    }

    public async Task<IReadOnlyList<FeatureValueEntry>> GetListAsync(
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);
        var cacheKey = _cacheKeyContributor.GetScopeCacheKey(scope, normalizedProviderKey, normalizedTenantId);
        var cached = await _cacheService.GetAsync<List<FeatureValueEntry>>(ScopeCachePrefix, cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var features = await _featureRepository.GetListByScopeAsync(
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        var result = features
            .Select(MapToEntry)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _cacheService.SetAsync(ScopeCachePrefix, result, cacheKey);
        return result;
    }

    private static FeatureValueEntry MapToEntry(FeatureValue feature)
    {
        return new FeatureValueEntry
        {
            Name = feature.Name,
            Value = feature.Value,
            Scope = feature.Scope,
            ProviderKey = feature.ProviderKey,
            TenantId = feature.TenantId
        };
    }

    private static string NormalizeProviderKey(FeatureScope scope, string providerKey, string? tenantId)
    {
        return scope switch
        {
            FeatureScope.Global => string.Empty,
            FeatureScope.Tenant => Require(tenantId ?? providerKey, nameof(providerKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的功能特性作用域")
        };
    }

    private static string? NormalizeTenantId(FeatureScope scope, string? tenantId)
    {
        return scope switch
        {
            FeatureScope.Global => null,
            FeatureScope.Tenant => Require(tenantId, nameof(tenantId)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的功能特性作用域")
        };
    }

    private static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }
}
