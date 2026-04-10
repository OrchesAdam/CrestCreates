using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Application.Settings;

public class SettingValueResolver : ISettingValueResolver
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingStore _settingStore;

    public SettingValueResolver(
        ISettingDefinitionManager settingDefinitionManager,
        ISettingStore settingStore)
    {
        _settingDefinitionManager = settingDefinitionManager;
        _settingStore = settingStore;
    }

    public async Task<ResolvedSettingValue> ResolveAsync(
        string name,
        string? tenantId = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var definition = _settingDefinitionManager.GetOrNull(name)
                         ?? throw new InvalidOperationException($"未定义的设置项: {name}");

        var values = await ResolveValuesAsync([definition], tenantId, userId, cancellationToken);
        return values[0];
    }

    public async Task<IReadOnlyList<ResolvedSettingValue>> ResolveAllAsync(
        string? groupName = null,
        string? tenantId = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var definitions = _settingDefinitionManager.GetAll()
            .Where(definition => string.IsNullOrWhiteSpace(groupName) ||
                                 string.Equals(definition.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return await ResolveValuesAsync(definitions, tenantId, userId, cancellationToken);
    }

    private async Task<IReadOnlyList<ResolvedSettingValue>> ResolveValuesAsync(
        IReadOnlyList<CrestCreates.Domain.Shared.Settings.SettingDefinition> definitions,
        string? tenantId,
        string? userId,
        CancellationToken cancellationToken)
    {
        var globalValues = await _settingStore.GetListAsync(SettingScope.Global, string.Empty, cancellationToken: cancellationToken);
        var tenantValues = string.IsNullOrWhiteSpace(tenantId)
            ? Array.Empty<SettingValueEntry>()
            : (await _settingStore.GetListAsync(SettingScope.Tenant, tenantId.Trim(), tenantId.Trim(), cancellationToken)).ToArray();
        var userValues = string.IsNullOrWhiteSpace(userId)
            ? Array.Empty<SettingValueEntry>()
            : (await _settingStore.GetListAsync(SettingScope.User, userId.Trim(), string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(), cancellationToken)).ToArray();

        var globalLookup = globalValues.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var tenantLookup = tenantValues.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var userLookup = userValues.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        return definitions
            .Select(definition => ResolveValue(definition, userLookup, tenantLookup, globalLookup))
            .ToArray();
    }

    private static ResolvedSettingValue ResolveValue(
        CrestCreates.Domain.Shared.Settings.SettingDefinition definition,
        IReadOnlyDictionary<string, SettingValueEntry> userLookup,
        IReadOnlyDictionary<string, SettingValueEntry> tenantLookup,
        IReadOnlyDictionary<string, SettingValueEntry> globalLookup)
    {
        if (userLookup.TryGetValue(definition.Name, out var userValue))
        {
            return new ResolvedSettingValue
            {
                Name = definition.Name,
                Value = userValue.Value,
                Scope = SettingScope.User,
                ProviderKey = userValue.ProviderKey,
                TenantId = userValue.TenantId,
                IsEncrypted = definition.IsEncrypted
            };
        }

        if (tenantLookup.TryGetValue(definition.Name, out var tenantValue))
        {
            return new ResolvedSettingValue
            {
                Name = definition.Name,
                Value = tenantValue.Value,
                Scope = SettingScope.Tenant,
                ProviderKey = tenantValue.ProviderKey,
                TenantId = tenantValue.TenantId,
                IsEncrypted = definition.IsEncrypted
            };
        }

        if (globalLookup.TryGetValue(definition.Name, out var globalValue))
        {
            return new ResolvedSettingValue
            {
                Name = definition.Name,
                Value = globalValue.Value,
                Scope = SettingScope.Global,
                ProviderKey = globalValue.ProviderKey,
                TenantId = null,
                IsEncrypted = definition.IsEncrypted
            };
        }

        return new ResolvedSettingValue
        {
            Name = definition.Name,
            Value = definition.DefaultValue,
            Scope = null,
            ProviderKey = null,
            TenantId = null,
            IsEncrypted = definition.IsEncrypted
        };
    }
}
