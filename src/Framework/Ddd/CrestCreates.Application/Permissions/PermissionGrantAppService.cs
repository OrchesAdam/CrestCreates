using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Permissions;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Permissions;

[CrestService]
public class PermissionGrantAppService : IPermissionGrantAppService
{
    private readonly IPermissionGrantManager _permissionGrantManager;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IPermissionChecker _permissionChecker;

    public PermissionGrantAppService(
        IPermissionGrantManager permissionGrantManager,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ICurrentTenant currentTenant,
        IPermissionChecker permissionChecker)
    {
        _permissionGrantManager = permissionGrantManager;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _currentTenant = currentTenant;
        _permissionChecker = permissionChecker;
    }

    public async Task GrantToUserAsync(
        string userId,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        var grant = CreateGrantInfo(input, PermissionGrantProviderType.User, userId);
        await EnsureManageGrantedAsync();
        await EnsureTenantSubjectOwnershipAsync(grant, cancellationToken);
        await _permissionGrantManager.GrantAsync(grant, cancellationToken);
    }

    public async Task GrantToRoleAsync(
        string roleName,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        var grant = CreateGrantInfo(input, PermissionGrantProviderType.Role, roleName);
        await EnsureManageGrantedAsync();
        await EnsureTenantSubjectOwnershipAsync(grant, cancellationToken);
        await _permissionGrantManager.GrantAsync(grant, cancellationToken);
    }

    public async Task RevokeFromUserAsync(
        string userId,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        var grant = CreateGrantInfo(input, PermissionGrantProviderType.User, userId);
        await EnsureManageGrantedAsync();
        await EnsureTenantSubjectOwnershipAsync(grant, cancellationToken);
        await _permissionGrantManager.RevokeAsync(grant, cancellationToken);
    }

    public async Task RevokeFromRoleAsync(
        string roleName,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        var grant = CreateGrantInfo(input, PermissionGrantProviderType.Role, roleName);
        await EnsureManageGrantedAsync();
        await EnsureTenantSubjectOwnershipAsync(grant, cancellationToken);
        await _permissionGrantManager.RevokeAsync(grant, cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionGrantDto>> GetUserGrantsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadGrantedAsync();
        await EnsureTenantUserReadableAsync(userId, cancellationToken);

        var grants = await _permissionGrantManager.GetGrantsAsync(
            PermissionGrantProviderType.User,
            userId,
            cancellationToken);

        return grants
            .Select(MapToDto)
            .ToArray();
    }

    public async Task<IReadOnlyList<PermissionGrantDto>> GetRoleGrantsAsync(
        string roleName,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadGrantedAsync();
        await EnsureTenantRoleReadableAsync(roleName, cancellationToken);

        var grants = await _permissionGrantManager.GetGrantsAsync(
            PermissionGrantProviderType.Role,
            roleName,
            cancellationToken);

        return grants
            .Select(MapToDto)
            .ToArray();
    }

    public async Task<UserEffectivePermissionsDto> GetUserEffectivePermissionsAsync(
        string userId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadGrantedAsync();

        var effectiveTenantId = string.IsNullOrWhiteSpace(tenantId) ? _currentTenant.Id?.Trim() : tenantId.Trim();
        var currentTenantId = _currentTenant.Id?.Trim();
        if (!string.IsNullOrEmpty(currentTenantId) &&
            !string.Equals(currentTenantId, effectiveTenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("租户上下文只能读取本租户的有效权限");
        }

        await EnsureTenantUserReadableAsync(userId, cancellationToken);

        var roleNames = await GetRoleNamesAsync(userId, effectiveTenantId, cancellationToken);
        var permissions = await _permissionGrantManager.GetEffectivePermissionsAsync(
            userId,
            roleNames,
            effectiveTenantId,
            cancellationToken);

        return new UserEffectivePermissionsDto
        {
            UserId = userId,
            TenantId = effectiveTenantId,
            Permissions = permissions.ToArray()
        };
    }

    private async Task<IReadOnlyList<string>> GetRoleNamesAsync(
        string userId,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
        {
            return Array.Empty<string>();
        }

        var roles = await _roleRepository.GetByUserIdAsync(parsedUserId, cancellationToken);
        return roles
            .Where(role => string.IsNullOrWhiteSpace(tenantId) ||
                           string.Equals(role.TenantId?.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(role => !string.IsNullOrWhiteSpace(role.Name))
            .Select(role => role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task EnsureManageGrantedAsync()
    {
        await _permissionChecker.CheckAsync(PermissionManagementPermissions.Manage);
    }

    private async Task EnsureReadGrantedAsync()
    {
        await _permissionChecker.CheckAsync(PermissionManagementPermissions.Read);
    }

    private async Task EnsureTenantSubjectOwnershipAsync(
        PermissionGrantInfo grant,
        CancellationToken cancellationToken)
    {
        var currentTenantId = _currentTenant.Id?.Trim();
        if (string.IsNullOrEmpty(currentTenantId) ||
            grant.Scope != PermissionGrantScope.Tenant ||
            !string.Equals(currentTenantId, grant.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            // Cross-tenant and host-global decisions belong to TenantPermissionScopeValidator in the manager.
            return;
        }

        if (grant.ProviderType == PermissionGrantProviderType.User)
        {
            var user = await FindTenantUserAsync(grant.ProviderKey, grant.TenantId!, cancellationToken);
            if (user is null)
            {
                throw new InvalidOperationException(
                    $"用户 '{grant.ProviderKey}' 不属于租户 '{grant.TenantId}'，不能变更其权限");
            }
        }
        else
        {
            var role = await _roleRepository.FindByNameAsync(grant.ProviderKey, grant.TenantId!, cancellationToken);
            if (role is null)
            {
                throw new InvalidOperationException(
                    $"角色 '{grant.ProviderKey}' 不属于租户 '{grant.TenantId}'，不能变更其权限");
            }
        }
    }

    private async Task EnsureTenantUserReadableAsync(string userId, CancellationToken cancellationToken)
    {
        var currentTenantId = _currentTenant.Id?.Trim();
        if (string.IsNullOrEmpty(currentTenantId))
        {
            return;
        }

        var user = await FindTenantUserAsync(userId, currentTenantId, cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException($"用户 '{userId}' 不属于当前租户，不能读取其权限");
        }
    }

    private async Task EnsureTenantRoleReadableAsync(string roleName, CancellationToken cancellationToken)
    {
        var currentTenantId = _currentTenant.Id?.Trim();
        if (string.IsNullOrEmpty(currentTenantId))
        {
            return;
        }

        var role = await _roleRepository.FindByNameAsync(roleName, currentTenantId, cancellationToken);
        if (role is null)
        {
            throw new InvalidOperationException($"角色 '{roleName}' 不属于当前租户，不能读取其权限");
        }
    }

    private async Task<User?> FindTenantUserAsync(
        string userId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
        {
            return null;
        }

        var user = await _userRepository.GetAsync(parsedUserId, cancellationToken);
        return user is not null &&
               string.Equals(user.TenantId?.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase)
            ? user
            : null;
    }

    private static PermissionGrantInfo CreateGrantInfo(
        PermissionGrantChangeDto input,
        PermissionGrantProviderType providerType,
        string providerKey)
    {
        return PermissionGrantNormalizer.Normalize(new PermissionGrantInfo
        {
            PermissionName = input.PermissionName,
            ProviderType = providerType,
            ProviderKey = providerKey,
            Scope = input.Scope,
            TenantId = input.TenantId
        });
    }

    private static PermissionGrantDto MapToDto(PermissionGrantInfo grant)
    {
        return new PermissionGrantDto
        {
            PermissionName = grant.PermissionName,
            ProviderType = grant.ProviderType,
            ProviderKey = grant.ProviderKey,
            Scope = grant.Scope,
            TenantId = grant.TenantId
        };
    }
}
