using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Identity;

[CrestService]
public class RoleAppService : IRoleAppService
{
    private readonly IRoleRepository _roleRepository;
    private readonly ICurrentTenant _currentTenant;

    public RoleAppService(IRoleRepository roleRepository, ICurrentTenant currentTenant)
    {
        _roleRepository = roleRepository;
        _currentTenant = currentTenant;
    }

    public async Task<IdentityRoleDto> CreateAsync(
        CreateIdentityRoleDto input,
        CancellationToken cancellationToken = default)
    {
        var roleName = NormalizeRequired(input.Name, nameof(input.Name));
        var tenantId = NormalizeRequired(input.TenantId, nameof(input.TenantId));

        var existingRole = await _roleRepository.FindByNameAsync(roleName, tenantId, cancellationToken);
        if (existingRole != null)
        {
            throw new InvalidOperationException($"角色 '{roleName}' 已存在");
        }

        var role = new Role(Guid.NewGuid(), roleName, tenantId)
        {
            DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim(),
            OrganizationId = input.OrganizationId,
            DataScope = input.DataScope,
            IsActive = true,
            CreationTime = DateTime.UtcNow
        };

        await _roleRepository.InsertAsync(role, cancellationToken);
        return MapToDto(role);
    }

    public async Task<IdentityRoleDto?> GetAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        return role == null ? null : MapToDto(role);
    }

    public async Task<IReadOnlyList<IdentityRoleDto>> GetListAsync(
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantId = string.IsNullOrWhiteSpace(tenantId) ? _currentTenant.Id : tenantId;
        var roles = string.IsNullOrWhiteSpace(effectiveTenantId)
            ? await _roleRepository.GetListAsync(cancellationToken)
            : await _roleRepository.GetListAsync(role => role.TenantId == effectiveTenantId, cancellationToken);

        return roles
            .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapToDto)
            .ToArray();
    }

    public async Task<IdentityRoleDto> UpdateAsync(
        Guid roleId,
        UpdateIdentityRoleDto input,
        CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetAsync(roleId, cancellationToken)
                   ?? throw new InvalidOperationException($"角色 '{roleId}' 不存在");

        role.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();
        role.OrganizationId = input.OrganizationId;
        role.DataScope = input.DataScope;
        role.LastModificationTime = DateTime.UtcNow;

        await _roleRepository.UpdateAsync(role, cancellationToken);
        return MapToDto(role);
    }

    public async Task SetActiveAsync(Guid roleId, bool isActive, CancellationToken cancellationToken = default)
    {
        var role = await _roleRepository.GetAsync(roleId, cancellationToken)
                   ?? throw new InvalidOperationException($"角色 '{roleId}' 不存在");

        role.IsActive = isActive;
        role.LastModificationTime = DateTime.UtcNow;
        await _roleRepository.UpdateAsync(role, cancellationToken);
    }

    private static IdentityRoleDto MapToDto(Role role)
    {
        return new IdentityRoleDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName,
            TenantId = role.TenantId,
            OrganizationId = role.OrganizationId,
            DataScope = role.DataScope,
            IsActive = role.IsActive
        };
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }
}
