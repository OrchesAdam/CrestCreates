using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestPermissionException = CrestCreates.Domain.Exceptions.CrestPermissionException;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Features;

[CrestService]
public class FeatureAppService : IFeatureAppService
{
    private readonly IFeatureManager _featureManager;
    private readonly IFeatureProvider _featureProvider;
    private readonly IFeatureValueResolver _featureValueResolver;
    private readonly ICurrentTenant _currentTenant;
    private readonly FeatureValueAppServiceMapper _mapper;
    private readonly IPermissionChecker _permissionChecker;

    public FeatureAppService(
        IFeatureManager featureManager,
        IFeatureProvider featureProvider,
        IFeatureValueResolver featureValueResolver,
        ICurrentTenant currentTenant,
        FeatureValueAppServiceMapper mapper,
        IPermissionChecker permissionChecker)
    {
        _featureManager = featureManager;
        _featureProvider = featureProvider;
        _featureValueResolver = featureValueResolver;
        _currentTenant = currentTenant;
        _mapper = mapper;
        _permissionChecker = permissionChecker;
    }

    public Task<List<FeatureValueDto>> GetGlobalValuesAsync()
    {
        return GetScopedValuesAsync(FeatureScope.Global, string.Empty, null);
    }

    public async Task<List<FeatureValueDto>> GetTenantValuesAsync(string tenantId)
    {
        EnsureCurrentTenantOrHost(tenantId, FeatureManagementPermissions.Read);
        await EnsureGrantedAsync(FeatureManagementPermissions.Read);
        return await GetScopedValuesAsync(FeatureScope.Tenant, tenantId, tenantId);
    }

    public async Task<List<FeatureValueDto>> GetCurrentTenantValuesAsync()
    {
        var tenantId = _currentTenant.Id;
        var resolved = await _featureValueResolver.ResolveAllAsync(
            tenantId: string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim());

        return resolved.Select(_mapper.Map).ToList();
    }

    public Task<FeatureValueDto?> GetGlobalValueAsync(string name)
    {
        return GetScopedValueAsync(name, FeatureScope.Global, string.Empty, null);
    }

    public Task<FeatureValueDto?> GetTenantValueAsync(string name, string tenantId)
    {
        return GetScopedValueAsync(name, FeatureScope.Tenant, tenantId, tenantId);
    }

    public async Task<FeatureValueDto?> GetCurrentTenantValueAsync(string name)
    {
        var tenantId = string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id.Trim();
        var resolved = await _featureValueResolver.ResolveAsync(name, tenantId);
        return _mapper.Map(resolved);
    }

    public async Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default)
    {
        EnsureHostContext(FeatureManagementPermissions.ManageGlobal);
        await EnsureGrantedAsync(FeatureManagementPermissions.ManageGlobal);
        await _featureManager.SetGlobalAsync(name, value, cancellationToken);
    }

    public async Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default)
    {
        EnsureCurrentTenantOrHost(tenantId, FeatureManagementPermissions.ManageTenant);
        await EnsureGrantedAsync(FeatureManagementPermissions.ManageTenant);
        await _featureManager.SetTenantAsync(name, tenantId, value, cancellationToken);
    }

    public async Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureHostContext(FeatureManagementPermissions.ManageGlobal);
        await EnsureGrantedAsync(FeatureManagementPermissions.ManageGlobal);
        await _featureManager.RemoveGlobalAsync(name, cancellationToken);
    }

    public async Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
    {
        EnsureCurrentTenantOrHost(tenantId, FeatureManagementPermissions.ManageTenant);
        await EnsureGrantedAsync(FeatureManagementPermissions.ManageTenant);
        await _featureManager.RemoveTenantAsync(name, tenantId, cancellationToken);
    }

    public async Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default)
    {
        return await _featureProvider.GetAsync<bool>(featureName, cancellationToken);
    }

    public async Task<bool> IsTenantEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default)
    {
        EnsureCurrentTenantOrHost(tenantId, FeatureManagementPermissions.Read);
        await EnsureGrantedAsync(FeatureManagementPermissions.Read);
        var resolved = await _featureValueResolver.ResolveAsync(featureName, tenantId, cancellationToken);
        return bool.TryParse(resolved.Value, out var result) && result;
    }

    private async Task<List<FeatureValueDto>> GetScopedValuesAsync(
        FeatureScope scope,
        string providerKey,
        string? tenantId)
    {
        var values = await _featureManager.GetScopedValuesAsync(scope, providerKey, null, tenantId);
        return values.Select(_mapper.Map).ToList();
    }

    private async Task<FeatureValueDto?> GetScopedValueAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId)
    {
        var value = await _featureManager.GetScopedValueOrNullAsync(name, scope, providerKey, tenantId);
        return value == null ? null : _mapper.Map(value);
    }

    private async Task EnsureGrantedAsync(string permission)
    {
        if (!await _permissionChecker.IsGrantedAsync(permission))
        {
            throw new CrestPermissionException(permission);
        }
    }

    private void EnsureHostContext(string permission)
    {
        // Allow when there is no tenant context (truly host) or when the
        // current tenant is the special "host" tenant itself.
        var tenantName = _currentTenant.Tenant?.Name;
        if (tenantName is not null &&
            !string.Equals(tenantName, "host", StringComparison.OrdinalIgnoreCase))
        {
            throw new CrestPermissionException(permission);
        }
    }

    private void EnsureCurrentTenantOrHost(string targetTenantId, string permission)
    {
        // Allow if there's no tenant context (truly host).
        var currentTenant = _currentTenant.Tenant;
        if (currentTenant is null)
        {
            return;
        }

        // Allow if the current tenant is the special "host" tenant itself.
        if (string.Equals(currentTenant.Name, "host", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Otherwise, allow only if the target matches the current tenant.
        if (!string.Equals(currentTenant.Id, targetTenantId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new CrestPermissionException(permission);
        }
    }
}
