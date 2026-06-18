using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Settings;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Settings;

public class SettingProvider : ISettingProvider
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingValueResolver _settingValueResolver;
    private readonly SettingValueTypeConverter _settingValueTypeConverter;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public SettingProvider(
        ISettingDefinitionManager settingDefinitionManager,
        ISettingValueResolver settingValueResolver,
        SettingValueTypeConverter settingValueTypeConverter,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _settingDefinitionManager = settingDefinitionManager;
        _settingValueResolver = settingValueResolver;
        _settingValueTypeConverter = settingValueTypeConverter;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await _settingValueResolver.ResolveAsync(
            name,
            GetCurrentTenantIdOrNull(),
            GetCurrentUserIdOrNull(),
            cancellationToken);

        return result.Value;
    }

    public async Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        var definition = _settingDefinitionManager.GetOrNull(name)
                         ?? throw new InvalidOperationException($"未定义的设置项: {name}");

        var value = await GetOrNullAsync(name, cancellationToken);
        return _settingValueTypeConverter.ConvertTo<T>(value, definition.ValueType);
    }

    public Task<IReadOnlyList<ResolvedSettingValue>> GetAllAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        return _settingValueResolver.ResolveAllAsync(
            groupName,
            GetCurrentTenantIdOrNull(),
            GetCurrentUserIdOrNull(),
            cancellationToken);
    }

    private string? GetCurrentTenantIdOrNull()
    {
        return string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id;
    }

    private string? GetCurrentUserIdOrNull()
    {
        return _currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(_currentUser.Id)
            ? _currentUser.Id
            : null;
    }
}
