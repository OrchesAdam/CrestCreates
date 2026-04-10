using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Caching;

public class SettingCacheKeyContributor
{
    public string GetItemCacheKey(
        SettingScope scope,
        string providerKey,
        string settingName,
        string? tenantId = null)
    {
        return string.Join(
            ":",
            "Scope",
            scope,
            Normalize(providerKey),
            Normalize(tenantId),
            Normalize(settingName));
    }

    public string GetScopeCacheKey(
        SettingScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return string.Join(
            ":",
            "Scope",
            scope,
            Normalize(providerKey),
            Normalize(tenantId));
    }

    public string GetScopePattern(
        SettingScope scope,
        string providerKey,
        string? tenantId = null)
    {
        return string.Join(
            ":",
            "Setting",
            "*",
            "Scope",
            scope,
            Normalize(providerKey),
            Normalize(tenantId),
            "*");
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}
