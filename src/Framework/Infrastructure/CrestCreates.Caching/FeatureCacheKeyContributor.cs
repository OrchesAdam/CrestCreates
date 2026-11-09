using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Caching;

public class FeatureCacheKeyContributor
{
    public const string ItemCachePrefix = "Feature.Item";
    public const string ScopeCachePrefix = "Feature.Scope";

    public string GetItemCacheKey(
        FeatureScope scope,
        string providerKey,
        string featureName,
        string? tenantId = null)
    {
        return ItemCachePrefix + ":" + GetScopeKeyPart(scope, providerKey, tenantId) + ":" + Normalize(featureName);
    }

    public string GetScopeCacheKey(
        FeatureScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return ScopeCachePrefix + ":" + GetScopeKeyPart(scope, providerKey, tenantId);
    }

    public string GetScopeItemPattern(
        FeatureScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return ItemCachePrefix + ":" + GetEscapedScopeKeyPart(scope, providerKey, tenantId) + ":*";
    }

    public string GetTenantFeaturePattern(string tenantId)
    {
        return GetScopeItemPattern(FeatureScope.Tenant, tenantId, tenantId);
    }

    public string GetGlobalFeaturePattern()
    {
        return GetScopeItemPattern(FeatureScope.Global, string.Empty, null);
    }

    private static string GetScopeKeyPart(FeatureScope scope, string providerKey, string? tenantId)
    {
        return string.Join(":", "Scope", scope, Normalize(providerKey), Normalize(tenantId));
    }

    private static string GetEscapedScopeKeyPart(FeatureScope scope, string providerKey, string? tenantId)
    {
        return string.Join(
            ":",
            "Scope",
            scope,
            CacheGlob.Escape(Normalize(providerKey)),
            CacheGlob.Escape(Normalize(tenantId)));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}
