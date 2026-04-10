using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Application.Settings;

public class SettingStore : ISettingStore
{
    private const string ItemCachePrefix = "Setting.Item";
    private const string ScopeCachePrefix = "Setting.Scope";

    private readonly ISettingRepository _settingRepository;
    private readonly ISettingEncryptionService _settingEncryptionService;
    private readonly ICrestCacheService _cacheService;
    private readonly SettingCacheKeyContributor _cacheKeyContributor;

    public SettingStore(
        ISettingRepository settingRepository,
        ISettingEncryptionService settingEncryptionService,
        ICrestCacheService cacheService,
        SettingCacheKeyContributor cacheKeyContributor)
    {
        _settingRepository = settingRepository;
        _settingEncryptionService = settingEncryptionService;
        _cacheService = cacheService;
        _cacheKeyContributor = cacheKeyContributor;
    }

    public async Task<SettingValueEntry?> GetOrNullAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);
        var cacheKey = _cacheKeyContributor.GetItemCacheKey(scope, normalizedProviderKey, name, normalizedTenantId);
        var cached = await _cacheService.GetAsync<SettingValueEntry?>(ItemCachePrefix, cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var setting = await _settingRepository.FindAsync(
            name.Trim(),
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        if (setting is null)
        {
            return null;
        }

        var entry = MapToEntry(setting);
        await _cacheService.SetAsync(ItemCachePrefix, entry, cacheKey);
        return entry;
    }

    public async Task<IReadOnlyList<SettingValueEntry>> GetListAsync(
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);
        var cacheKey = _cacheKeyContributor.GetScopeCacheKey(scope, normalizedProviderKey, normalizedTenantId);
        var cached = await _cacheService.GetAsync<List<SettingValueEntry>>(ScopeCachePrefix, cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var settings = await _settingRepository.GetListByScopeAsync(
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        var result = settings
            .Select(MapToEntry)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _cacheService.SetAsync(ScopeCachePrefix, result, cacheKey);
        return result;
    }

    private SettingValueEntry MapToEntry(SettingValue setting)
    {
        return new SettingValueEntry
        {
            Name = setting.Name,
            Value = setting.IsEncrypted ? _settingEncryptionService.Unprotect(setting.Value) : setting.Value,
            ProviderType = setting.ProviderType,
            Scope = setting.Scope,
            ProviderKey = setting.ProviderKey,
            TenantId = setting.TenantId,
            IsEncrypted = setting.IsEncrypted
        };
    }

    private static string NormalizeProviderKey(SettingScope scope, string providerKey, string? tenantId)
    {
        return scope switch
        {
            SettingScope.Global => string.Empty,
            SettingScope.Tenant => Require(tenantId ?? providerKey, nameof(providerKey)),
            SettingScope.User => Require(providerKey, nameof(providerKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的设置作用域")
        };
    }

    private static string? NormalizeTenantId(SettingScope scope, string? tenantId)
    {
        return scope switch
        {
            SettingScope.Global => null,
            SettingScope.Tenant => Require(tenantId, nameof(tenantId)),
            SettingScope.User => string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的设置作用域")
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
