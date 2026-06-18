using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Permissions;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
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
    private readonly ICurrentTenant _currentTenant;

    public PermissionGrantAppService(
        IPermissionGrantManager permissionGrantManager,
        IRoleRepository roleRepository,
        ICurrentTenant currentTenant)
    {
        _permissionGrantManager = permissionGrantManager;
        _roleRepository = roleRepository;
        _currentTenant = currentTenant;
    }

    public Task GrantToUserAsync(
        string userId,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        return _permissionGrantManager.GrantAsync(
            CreateGrantInfo(input, PermissionGrantProviderType.User, userId),
            cancellationToken);
    }

    public Task GrantToRoleAsync(
        string roleName,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        return _permissionGrantManager.GrantAsync(
            CreateGrantInfo(input, PermissionGrantProviderType.Role, roleName),
            cancellationToken);
    }

    public Task RevokeFromUserAsync(
        string userId,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        return _permissionGrantManager.RevokeAsync(
            CreateGrantInfo(input, PermissionGrantProviderType.User, userId),
            cancellationToken);
    }

    public Task RevokeFromRoleAsync(
        string roleName,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default)
    {
        return _permissionGrantManager.RevokeAsync(
            CreateGrantInfo(input, PermissionGrantProviderType.Role, roleName),
            cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionGrantDto>> GetUserGrantsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
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
        var effectiveTenantId = string.IsNullOrWhiteSpace(tenantId) ? _currentTenant.Id : tenantId;
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
                           string.Equals(role.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            .Where(role => !string.IsNullOrWhiteSpace(role.Name))
            .Select(role => role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PermissionGrantInfo CreateGrantInfo(
        PermissionGrantChangeDto input,
        PermissionGrantProviderType providerType,
        string providerKey)
    {
        return new PermissionGrantInfo
        {
            PermissionName = input.PermissionName,
            ProviderType = providerType,
            ProviderKey = providerKey,
            Scope = input.Scope,
            TenantId = input.TenantId
        };
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
