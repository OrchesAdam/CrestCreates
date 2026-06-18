using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;

namespace CrestCreates.Application.Contracts.Interfaces;

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
