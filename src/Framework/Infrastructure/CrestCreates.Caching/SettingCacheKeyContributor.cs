using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Caching;

public class SettingCacheKeyContributor
{
    public const string ItemCachePrefix = "Setting.Item";
    public const string ScopeCachePrefix = "Setting.Scope";

    public string GetItemCacheKey(
        SettingScope scope,
        string providerKey,
        string settingName,
        string? tenantId = null)
    {
        return ItemCachePrefix + ":" + GetScopeKeyPart(scope, providerKey, tenantId) + ":" + Normalize(settingName);
    }

    public string GetScopeCacheKey(
        SettingScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return ScopeCachePrefix + ":" + GetScopeKeyPart(scope, providerKey, tenantId);
    }

    public string GetScopeItemPattern(
        SettingScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return ItemCachePrefix + ":" + GetEscapedScopeKeyPart(scope, providerKey, tenantId) + ":*";
    }

    private static string GetScopeKeyPart(SettingScope scope, string providerKey, string? tenantId)
    {
        return string.Join(":", "Scope", scope, Normalize(providerKey), Normalize(tenantId));
    }

    private static string GetEscapedScopeKeyPart(SettingScope scope, string providerKey, string? tenantId)
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
