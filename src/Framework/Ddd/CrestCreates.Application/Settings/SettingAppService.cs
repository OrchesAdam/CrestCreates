using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories.Permission;
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
    private readonly IPermissionChecker _permissionChecker;
    private readonly IUserRepository _userRepository;

    public SettingAppService(
        ISettingProvider settingProvider,
        ISettingManager settingManager,
        ISettingDefinitionManager settingDefinitionManager,
        SettingValueAppServiceMapper mapper,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IPermissionChecker permissionChecker,
        IUserRepository userRepository)
    {
        _settingProvider = settingProvider;
        _settingManager = settingManager;
        _settingDefinitionManager = settingDefinitionManager;
        _mapper = mapper;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _permissionChecker = permissionChecker;
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<SettingValueDto>> GetCurrentValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        var values = await _settingProvider.GetAllAsync(groupName, cancellationToken);
        return values.Select(_mapper.Map).ToArray();
    }

    public async Task<IReadOnlyList<SettingValueDto>> GetGlobalValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadGrantedAsync();
        EnsureGlobalManagementAllowed();
        return await GetScopedValuesAsync(SettingScope.Global, string.Empty, groupName, null, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingValueDto>> GetCurrentTenantValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadGrantedAsync();
        var tenantId = GetRequiredCurrentTenantId();
        return await GetScopedValuesAsync(SettingScope.Tenant, tenantId, groupName, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingValueDto>> GetTenantValuesAsync(
        string tenantId,
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadGrantedAsync();
        var normalizedTenantId = Require(tenantId, nameof(tenantId));
        EnsureTenantOperable(normalizedTenantId);
        return await GetScopedValuesAsync(SettingScope.Tenant, normalizedTenantId, groupName, normalizedTenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingValueDto>> GetCurrentUserValuesAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        var userId = GetRequiredCurrentUserId();
        var tenantId = GetCurrentTenantIdOrNull();
        return await GetScopedValuesAsync(SettingScope.User, userId, groupName, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingValueDto>> GetUserValuesAsync(
        string userId,
        string? tenantId = null,
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadGrantedAsync();
        var normalizedUserId = Require(userId, nameof(userId));
        var normalizedTenantId = NormalizeTenantId(tenantId);
        await EnsureUserOperableAsync(normalizedUserId, normalizedTenantId, cancellationToken);
        return await GetScopedValuesAsync(SettingScope.User, normalizedUserId, groupName, normalizedTenantId, cancellationToken);
    }

    public async Task UpdateGlobalAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        EnsureGlobalManagementAllowed();
        await _settingManager.SetGlobalAsync(name, input.Value, cancellationToken);
    }

    public async Task UpdateCurrentTenantAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        await _settingManager.SetTenantAsync(name, GetRequiredCurrentTenantId(), input.Value, cancellationToken);
    }

    public async Task UpdateTenantAsync(string tenantId, string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        var normalizedTenantId = Require(tenantId, nameof(tenantId));
        EnsureTenantOperable(normalizedTenantId);
        await _settingManager.SetTenantAsync(name, normalizedTenantId, input.Value, cancellationToken);
    }

    public async Task UpdateCurrentUserAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        await _settingManager.SetUserAsync(name, GetRequiredCurrentUserId(), input.Value, GetCurrentTenantIdOrNull(), cancellationToken);
    }

    public async Task UpdateUserAsync(
        string userId,
        string name,
        UpdateSettingValueDto input,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        var normalizedUserId = Require(userId, nameof(userId));
        var normalizedTenantId = NormalizeTenantId(tenantId);
        await EnsureUserOperableAsync(normalizedUserId, normalizedTenantId, cancellationToken);
        await _settingManager.SetUserAsync(name, normalizedUserId, input.Value, normalizedTenantId, cancellationToken);
    }

    public async Task DeleteGlobalAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        EnsureGlobalManagementAllowed();
        await _settingManager.RemoveGlobalAsync(name, cancellationToken);
    }

    public async Task DeleteCurrentTenantAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        await _settingManager.RemoveTenantAsync(name, GetRequiredCurrentTenantId(), cancellationToken);
    }

    public async Task DeleteTenantAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        var normalizedTenantId = Require(tenantId, nameof(tenantId));
        EnsureTenantOperable(normalizedTenantId);
        await _settingManager.RemoveTenantAsync(name, normalizedTenantId, cancellationToken);
    }

    public async Task DeleteCurrentUserAsync(string name, CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        await _settingManager.RemoveUserAsync(name, GetRequiredCurrentUserId(), GetCurrentTenantIdOrNull(), cancellationToken);
    }

    public async Task DeleteUserAsync(
        string userId,
        string name,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureManageGrantedAsync();
        var normalizedUserId = Require(userId, nameof(userId));
        var normalizedTenantId = NormalizeTenantId(tenantId);
        await EnsureUserOperableAsync(normalizedUserId, normalizedTenantId, cancellationToken);
        await _settingManager.RemoveUserAsync(name, normalizedUserId, normalizedTenantId, cancellationToken);
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

    private async Task EnsureReadGrantedAsync()
    {
        await _permissionChecker.CheckAsync(SettingManagementPermissions.Read).ConfigureAwait(false);
    }

    private async Task EnsureManageGrantedAsync()
    {
        await _permissionChecker.CheckAsync(SettingManagementPermissions.Manage).ConfigureAwait(false);
    }

    private void RequireAuthenticated()
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.Id))
        {
            throw new UnauthorizedAccessException("当前用户上下文不存在");
        }
    }

    private void EnsureGlobalManagementAllowed()
    {
        if (_currentUser.IsSuperAdmin || string.IsNullOrWhiteSpace(_currentTenant.Id))
        {
            return;
        }

        throw new InvalidOperationException("租户上下文不能管理全局设置");
    }

    private void EnsureTenantOperable(string targetTenantId)
    {
        if (_currentUser.IsSuperAdmin)
        {
            return;
        }

        var currentTenantId = NormalizeTenantId(_currentTenant.Id);
        if (currentTenantId is null)
        {
            return;
        }

        if (!string.Equals(currentTenantId, NormalizeTenantId(targetTenantId), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("租户上下文只能管理本租户设置");
        }
    }

    private async Task EnsureUserOperableAsync(
        string userId,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var targetTenantId = tenantId ?? NormalizeTenantId(_currentTenant.Id);
        EnsureTenantOperable(targetTenantId ?? string.Empty);

        if (!Guid.TryParse(userId, out var parsedUserId))
        {
            throw new InvalidOperationException($"用户 '{userId}' 不存在");
        }

        var user = await _userRepository.GetAsync(parsedUserId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"用户 '{userId}' 不存在");

        if (!string.Equals(NormalizeTenantId(user.TenantId), targetTenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标用户不属于指定租户");
        }
    }

    private static string? NormalizeTenantId(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
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
        return NormalizeTenantId(_currentTenant.Id);
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
