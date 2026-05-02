using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantAppService
{
    Task<TenantDto> CreateAsync(CreateTenantDto input, CancellationToken cancellationToken = default);
    Task<TenantDto?> GetAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantDto>> GetListAsync(bool? isActive = null, CancellationToken cancellationToken = default);
    Task<TenantDto> UpdateAsync(string name, UpdateTenantDto input, CancellationToken cancellationToken = default);
    Task SetActiveAsync(string name, bool isActive, CancellationToken cancellationToken = default);

    Task<TenantInitializationResult> RetryInitializationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<TenantInitializationResult> GetInitializationStatusAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<TenantInitializationResult> ForceRetryInitializationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task ForceFailInitializationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
