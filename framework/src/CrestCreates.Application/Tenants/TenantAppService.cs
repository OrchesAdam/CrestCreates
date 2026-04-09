using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Tenants;

[CrestService]
public class TenantAppService : ITenantAppService
{
    private readonly ITenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;

    public TenantAppService(
        ITenantManager tenantManager,
        ITenantRepository tenantRepository)
    {
        _tenantManager = tenantManager;
        _tenantRepository = tenantRepository;
    }

    public async Task<TenantDto> CreateAsync(
        CreateTenantDto input,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantManager.CreateAsync(
            input.Name,
            input.DisplayName,
            input.DefaultConnectionString,
            cancellationToken);

        return MapToDto(tenant);
    }

    public async Task<TenantDto?> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(
            NormalizeRequired(name, nameof(name)),
            cancellationToken);

        return tenant == null ? null : MapToDto(tenant);
    }

    public async Task<IReadOnlyList<TenantDto>> GetListAsync(
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantRepository.GetListWithDetailsAsync(cancellationToken);
        return tenants
            .Where(tenant => !isActive.HasValue || tenant.IsActive == isActive.Value)
            .OrderBy(tenant => tenant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapToDto)
            .ToArray();
    }

    public async Task<TenantDto> UpdateAsync(
        string name,
        UpdateTenantDto input,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantManager.UpdateAsync(
            NormalizeRequired(name, nameof(name)),
            input.DisplayName,
            input.DefaultConnectionString,
            cancellationToken);

        return MapToDto(tenant);
    }

    public Task SetActiveAsync(
        string name,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        return _tenantManager.SetActiveAsync(
            NormalizeRequired(name, nameof(name)),
            isActive,
            cancellationToken);
    }

    private static TenantDto MapToDto(Tenant tenant)
    {
        return new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            DisplayName = tenant.DisplayName,
            DefaultConnectionString = tenant.GetDefaultConnectionString(),
            IsActive = tenant.IsActive,
            CreationTime = tenant.CreationTime,
            LastModificationTime = tenant.LastModificationTime
        };
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }
}
