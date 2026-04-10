using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Settings;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Settings;

[CrestService]
public class SettingAppService : ISettingAppService
{
    private readonly ISettingProvider _settingProvider;
    private readonly ISettingManager _settingManager;
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly SettingValueAppServiceMapper _mapper;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public SettingAppService(
        ISettingProvider settingProvider,
        ISettingManager settingManager,
        ISettingDefinitionManager settingDefinitionManager,
        SettingValueAppServiceMapper mapper,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _settingProvider = settingProvider;
        _settingManager = settingManager;
        _settingDefinitionManager = settingDefinitionManager;
        _mapper = mapper;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<SettingValueDto>> GetCurrentValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        var values = await _settingProvider.GetAllAsync(groupName, cancellationToken);
        return values.Select(_mapper.Map).ToArray();
    }

    public Task<IReadOnlyList<SettingValueDto>> GetGlobalValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        return GetScopedValuesAsync(SettingScope.Global, string.Empty, groupName, null, cancellationToken);
    }

    public Task<IReadOnlyList<SettingValueDto>> GetCurrentTenantValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = GetRequiredCurrentTenantId();
        return GetScopedValuesAsync(SettingScope.Tenant, tenantId, groupName, tenantId, cancellationToken);
    }

    public Task<IReadOnlyList<SettingValueDto>> GetTenantValuesAsync(
        string tenantId,
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedTenantId = Require(tenantId, nameof(tenantId));
        return GetScopedValuesAsync(SettingScope.Tenant, normalizedTenantId, groupName, normalizedTenantId, cancellationToken);
    }

    public Task<IReadOnlyList<SettingValueDto>> GetCurrentUserValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredCurrentUserId();
        var tenantId = GetCurrentTenantIdOrNull();
        return GetScopedValuesAsync(SettingScope.User, userId, groupName, tenantId, cancellationToken);
    }

    public Task<IReadOnlyList<SettingValueDto>> GetUserValuesAsync(
        string userId,
        string? tenantId = null,
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        return GetScopedValuesAsync(
            SettingScope.User,
            Require(userId, nameof(userId)),
            groupName,
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            cancellationToken);
    }

    public Task UpdateGlobalAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        return _settingManager.SetGlobalAsync(name, input.Value, cancellationToken);
    }

    public Task UpdateCurrentTenantAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        return _settingManager.SetTenantAsync(name, GetRequiredCurrentTenantId(), input.Value, cancellationToken);
    }

    public Task UpdateTenantAsync(string tenantId, string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        return _settingManager.SetTenantAsync(name, Require(tenantId, nameof(tenantId)), input.Value, cancellationToken);
    }

    public Task UpdateCurrentUserAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        return _settingManager.SetUserAsync(name, GetRequiredCurrentUserId(), input.Value, GetCurrentTenantIdOrNull(), cancellationToken);
    }

    public Task UpdateUserAsync(
        string userId,
        string name,
        UpdateSettingValueDto input,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return _settingManager.SetUserAsync(
            name,
            Require(userId, nameof(userId)),
            input.Value,
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            cancellationToken);
    }

    public Task DeleteGlobalAsync(string name, CancellationToken cancellationToken = default)
    {
        return _settingManager.RemoveGlobalAsync(name, cancellationToken);
    }

    public Task DeleteCurrentTenantAsync(string name, CancellationToken cancellationToken = default)
    {
        return _settingManager.RemoveTenantAsync(name, GetRequiredCurrentTenantId(), cancellationToken);
    }

    public Task DeleteTenantAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        return _settingManager.RemoveTenantAsync(name, Require(tenantId, nameof(tenantId)), cancellationToken);
    }

    public Task DeleteCurrentUserAsync(string name, CancellationToken cancellationToken = default)
    {
        return _settingManager.RemoveUserAsync(name, GetRequiredCurrentUserId(), GetCurrentTenantIdOrNull(), cancellationToken);
    }

    public Task DeleteUserAsync(
        string userId,
        string name,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return _settingManager.RemoveUserAsync(
            name,
            Require(userId, nameof(userId)),
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            cancellationToken);
    }

    private async Task<IReadOnlyList<SettingValueDto>> GetScopedValuesAsync(
        SettingScope scope,
        string providerKey,
        string? groupName,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var definitions = _settingDefinitionManager.GetAll()
            .Where(definition => definition.SupportsScope(scope))
            .Where(definition => string.IsNullOrWhiteSpace(groupName) ||
                                 string.Equals(definition.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var values = await _settingManager.GetScopedValuesAsync(scope, providerKey, groupName, tenantId, cancellationToken);
        var lookup = values.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);

        return definitions
            .Select(definition => _mapper.Map(lookup.TryGetValue(definition.Name, out var value) ? value : null, definition.Name))
            .ToArray();
    }

    private string GetRequiredCurrentTenantId()
    {
        if (string.IsNullOrWhiteSpace(_currentTenant.Id))
        {
            throw new InvalidOperationException("当前租户上下文不存在");
        }

        return _currentTenant.Id.Trim();
    }

    private string GetRequiredCurrentUserId()
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.Id))
        {
            throw new InvalidOperationException("当前用户上下文不存在");
        }

        return _currentUser.Id.Trim();
    }

    private string? GetCurrentTenantIdOrNull()
    {
        return string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id.Trim();
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
