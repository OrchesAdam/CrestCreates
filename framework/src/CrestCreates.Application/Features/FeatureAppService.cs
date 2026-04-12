using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Domain.Features;
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

    public FeatureAppService(
        IFeatureManager featureManager,
        IFeatureProvider featureProvider,
        IFeatureValueResolver featureValueResolver,
        ICurrentTenant currentTenant)
    {
        _featureManager = featureManager;
        _featureProvider = featureProvider;
        _featureValueResolver = featureValueResolver;
        _currentTenant = currentTenant;
    }

    public Task<List<FeatureValueDto>> GetGlobalValuesAsync()
    {
        return GetScopedValuesAsync(FeatureScope.Global, string.Empty, null);
    }

    public Task<List<FeatureValueDto>> GetTenantValuesAsync(string tenantId)
    {
        return GetScopedValuesAsync(FeatureScope.Tenant, tenantId, tenantId);
    }

    public Task<List<FeatureValueDto>> GetCurrentTenantValuesAsync()
    {
        var tenantId = _currentTenant.Id;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return GetGlobalValuesAsync();
        }

        return GetTenantValuesAsync(tenantId);
    }

    public Task<FeatureValueDto?> GetGlobalValueAsync(string name)
    {
        return GetScopedValueAsync(name, FeatureScope.Global, string.Empty, null);
    }

    public Task<FeatureValueDto?> GetTenantValueAsync(string name, string tenantId)
    {
        return GetScopedValueAsync(name, FeatureScope.Tenant, tenantId, tenantId);
    }

    public Task<FeatureValueDto?> GetCurrentTenantValueAsync(string name)
    {
        var tenantId = _currentTenant.Id;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return GetGlobalValueAsync(name);
        }

        return GetTenantValueAsync(name, tenantId);
    }

    public Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default)
    {
        return _featureManager.SetGlobalAsync(name, value, cancellationToken);
    }

    public Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default)
    {
        return _featureManager.SetTenantAsync(name, tenantId, value, cancellationToken);
    }

    public Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default)
    {
        return _featureManager.RemoveGlobalAsync(name, cancellationToken);
    }

    public Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
    {
        return _featureManager.RemoveTenantAsync(name, tenantId, cancellationToken);
    }

    public async Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default)
    {
        return await _featureProvider.GetAsync<bool>(featureName, cancellationToken);
    }

    public async Task<bool> IsTenantEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default)
    {
        var resolved = await _featureValueResolver.ResolveAsync(featureName, tenantId, cancellationToken);
        if (bool.TryParse(resolved.Value, out var result))
        {
            return result;
        }

        return false;
    }

    private async Task<List<FeatureValueDto>> GetScopedValuesAsync(
        FeatureScope scope,
        string providerKey,
        string? tenantId)
    {
        var values = await _featureManager.GetScopedValuesAsync(scope, providerKey, null, tenantId);
        var dtos = new List<FeatureValueDto>();

        foreach (var value in values)
        {
            dtos.Add(new FeatureValueDto
            {
                Name = value.Name,
                Value = value.Value,
                Scope = value.Scope,
                ProviderKey = value.ProviderKey,
                TenantId = value.TenantId
            });
        }

        return dtos;
    }

    private async Task<FeatureValueDto?> GetScopedValueAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId)
    {
        var value = await _featureManager.GetScopedValueOrNullAsync(name, scope, providerKey, tenantId);
        if (value == null)
        {
            return null;
        }

        return new FeatureValueDto
        {
            Name = value.Name,
            Value = value.Value,
            Scope = value.Scope,
            ProviderKey = value.ProviderKey,
            TenantId = value.TenantId
        };
    }
}

public interface IFeatureAppService
{
    Task<List<FeatureValueDto>> GetGlobalValuesAsync();
    Task<List<FeatureValueDto>> GetTenantValuesAsync(string tenantId);
    Task<List<FeatureValueDto>> GetCurrentTenantValuesAsync();
    Task<FeatureValueDto?> GetGlobalValueAsync(string name);
    Task<FeatureValueDto?> GetTenantValueAsync(string name, string tenantId);
    Task<FeatureValueDto?> GetCurrentTenantValueAsync(string name);
    Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default);
    Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default);
    Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default);
    Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default);
    Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default);
    Task<bool> IsTenantEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default);
}
