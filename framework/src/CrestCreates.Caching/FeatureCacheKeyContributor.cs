using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Caching;

public class FeatureCacheKeyContributor
{
    public string GetItemCacheKey(
        FeatureScope scope,
        string providerKey,
        string featureName,
        string? tenantId = null)
    {
        return string.Join(
            ":",
            "Feature",
            "Scope",
            scope,
            Normalize(providerKey),
            Normalize(tenantId),
            Normalize(featureName));
    }

    public string GetScopeCacheKey(
        FeatureScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return string.Join(
            ":",
            "Feature",
            "Scope",
            scope,
            Normalize(providerKey),
            Normalize(tenantId));
    }

    public string GetScopePattern(
        FeatureScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return string.Join(
            ":",
            "Feature",
            "*",
            "Scope",
            scope,
            Normalize(providerKey),
            Normalize(tenantId),
            "*");
    }

    public string GetTenantFeaturePattern(string tenantId)
    {
        return string.Join(
            ":",
            "Feature",
            "*",
            "Scope",
            FeatureScope.Tenant,
            Normalize(tenantId),
            "*");
    }

    public string GetGlobalFeaturePattern()
    {
        return string.Join(
            ":",
            "Feature",
            "*",
            "Scope",
            FeatureScope.Global,
            "*");
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}
