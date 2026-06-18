namespace CrestCreates.Caching;

public class TenantCacheKeyContributor
{
    public const string TenantKeyPrefix = "Tenant";

    public string GetTenantCacheKey(string? tenantId, params object[] parts)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return BuildCacheKey(parts);
        }

        return BuildCacheKey(PrependTenant(tenantId, parts));
    }

    public string GetPermissionCacheKey(string? tenantId, string providerType, string providerKey)
    {
        return GetTenantCacheKey(tenantId, "Permission", providerType, providerKey);
    }

    public string GetAuthorizationCacheKey(string? tenantId, string category, string key)
    {
        return GetTenantCacheKey(tenantId, "Authorization", category, key);
    }

    private static string[] PrependTenant(string tenantId, params object[] parts)
    {
        var result = new string[parts.Length + 2];
        result[0] = TenantKeyPrefix;
        result[1] = tenantId;
        for (int i = 0; i < parts.Length; i++)
        {
            result[i + 2] = parts[i]?.ToString() ?? string.Empty;
        }
        return result;
    }

    private static string BuildCacheKey(params object[] parts)
    {
        return string.Join(":", parts);
    }
}
