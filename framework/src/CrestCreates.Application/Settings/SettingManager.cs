using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Application.Settings;

public class SettingManager : ISettingManager
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingRepository _settingRepository;
    private readonly ISettingStore _settingStore;
    private readonly ISettingEncryptionService _settingEncryptionService;
    private readonly SettingValueTypeConverter _settingValueTypeConverter;
    private readonly SettingCacheInvalidator _settingCacheInvalidator;

    public SettingManager(
        ISettingDefinitionManager settingDefinitionManager,
        ISettingRepository settingRepository,
        ISettingStore settingStore,
        ISettingEncryptionService settingEncryptionService,
        SettingValueTypeConverter settingValueTypeConverter,
        SettingCacheInvalidator settingCacheInvalidator)
    {
        _settingDefinitionManager = settingDefinitionManager;
        _settingRepository = settingRepository;
        _settingStore = settingStore;
        _settingEncryptionService = settingEncryptionService;
        _settingValueTypeConverter = settingValueTypeConverter;
        _settingCacheInvalidator = settingCacheInvalidator;
    }

    public Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default)
    {
        return SetAsync(name, SettingScope.Global, string.Empty, value, null, cancellationToken);
    }

    public Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default)
    {
        return SetAsync(name, SettingScope.Tenant, tenantId, value, tenantId, cancellationToken);
    }

    public Task SetUserAsync(
        string name,
        string userId,
        string? value,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return SetAsync(name, SettingScope.User, userId, value, tenantId, cancellationToken);
    }

    public Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default)
    {
        return RemoveAsync(name, SettingScope.Global, string.Empty, null, cancellationToken);
    }

    public Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
    {
        return RemoveAsync(name, SettingScope.Tenant, tenantId, tenantId, cancellationToken);
    }

    public Task RemoveUserAsync(
        string name,
        string userId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return RemoveAsync(name, SettingScope.User, userId, tenantId, cancellationToken);
    }

    public Task<SettingValueEntry?> GetScopedValueOrNullAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureDefinitionExists(name);
        return _settingStore.GetOrNullAsync(name.Trim(), scope, providerKey, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingValueEntry>> GetScopedValuesAsync(
        SettingScope scope,
        string providerKey,
        string? groupName = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var definitions = _settingDefinitionManager.GetAll()
            .Where(definition => definition.SupportsScope(scope))
            .Where(definition => string.IsNullOrWhiteSpace(groupName) ||
                                 string.Equals(definition.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var values = await _settingStore.GetListAsync(scope, providerKey, tenantId, cancellationToken);
        return values
            .Where(value => definitions.ContainsKey(value.Name))
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task SetAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? value,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        if (value is null)
        {
            throw new ArgumentException("设置值不能为空，请使用删除接口移除覆盖值", nameof(value));
        }

        var definition = EnsureDefinitionExists(name);
        EnsureScopeAllowed(definition, scope);
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);

        _settingValueTypeConverter.Validate(value, definition.ValueType, definition.Name);

        var existing = await _settingRepository.FindAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        var persistedValue = definition.IsEncrypted && value is not null
            ? _settingEncryptionService.Protect(value)
            : value;

        if (existing is null)
        {
            await _settingRepository.InsertAsync(
                new SettingValue(
                    Guid.NewGuid(),
                    definition.Name,
                    scope,
                    normalizedProviderKey,
                    persistedValue,
                    definition.IsEncrypted,
                    normalizedTenantId),
                cancellationToken);
        }
        else
        {
            existing.SetValue(persistedValue, definition.IsEncrypted);
            await _settingRepository.UpdateAsync(existing, cancellationToken);
        }

        await _settingCacheInvalidator.InvalidateAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);
    }

    private async Task RemoveAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var definition = EnsureDefinitionExists(name);
        EnsureScopeAllowed(definition, scope);
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);

        var existing = await _settingRepository.FindAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        if (existing is null)
        {
            return;
        }

        await _settingRepository.DeleteAsync(existing, cancellationToken);
        await _settingCacheInvalidator.InvalidateAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);
    }

    private CrestCreates.Domain.Shared.Settings.SettingDefinition EnsureDefinitionExists(string name)
    {
        return _settingDefinitionManager.GetOrNull(name)
               ?? throw new InvalidOperationException($"未定义的设置项: {name}");
    }

    private static void EnsureScopeAllowed(
        CrestCreates.Domain.Shared.Settings.SettingDefinition definition,
        SettingScope scope)
    {
        if (!definition.SupportsScope(scope))
        {
            throw new InvalidOperationException($"设置 '{definition.Name}' 不支持作用域 {scope}");
        }
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
