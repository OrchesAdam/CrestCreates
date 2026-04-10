using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Caching;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Tenants;

[CrestService]
public class TenantConnectionStringAppService : ITenantConnectionStringAppService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IConnectionStringProtector _protector;
    private readonly TenantCacheInvalidator _cacheInvalidator;
    private readonly TenantCacheKeyContributor _cacheKeyContributor;

    public TenantConnectionStringAppService(
        ITenantRepository tenantRepository,
        IConnectionStringProtector protector,
        TenantCacheInvalidator cacheInvalidator,
        TenantCacheKeyContributor cacheKeyContributor)
    {
        _tenantRepository = tenantRepository;
        _protector = protector;
        _cacheInvalidator = cacheInvalidator;
        _cacheKeyContributor = cacheKeyContributor;
    }

    public async Task<TenantConnectionStringDto> CreateAsync(
        string tenantName,
        CreateTenantConnectionStringDto input,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        if (tenant.GetConnectionString(input.Name) != null)
        {
            throw new InvalidOperationException($"租户 '{tenantName}' 已存在名为 '{input.Name}' 的连接串");
        }

        tenant.AddConnectionString(input.Name, input.Value);
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        await _cacheInvalidator.InvalidateConnectionStringAsync(tenant.Id.ToString(), input.Name, cancellationToken);

        return MapToDto(tenant, input.Name);
    }

    public async Task<TenantConnectionStringDto> UpdateAsync(
        string tenantName,
        string connectionStringName,
        UpdateTenantConnectionStringDto input,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        if (tenant.GetConnectionString(connectionStringName) == null)
        {
            throw new InvalidOperationException($"租户 '{tenantName}' 不存在名为 '{connectionStringName}' 的连接串");
        }

        tenant.AddConnectionString(connectionStringName, input.Value);
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        await _cacheInvalidator.InvalidateConnectionStringAsync(tenant.Id.ToString(), connectionStringName, cancellationToken);

        return MapToDto(tenant, connectionStringName);
    }

    public async Task DeleteAsync(
        string tenantName,
        string connectionStringName,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        if (!tenant.RemoveConnectionString(connectionStringName))
        {
            throw new InvalidOperationException($"租户 '{tenantName}' 不存在名为 '{connectionStringName}' 的连接串");
        }

        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        await _cacheInvalidator.InvalidateConnectionStringAsync(tenant.Id.ToString(), connectionStringName, cancellationToken);
    }

    public async Task<IReadOnlyList<TenantConnectionStringDto>> GetListAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(tenantName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{tenantName}' 不存在");

        return tenant.ConnectionStrings.Select(cs => MapToDto(tenant, cs.Name)).ToArray();
    }

    private TenantConnectionStringDto MapToDto(Tenant tenant, string connectionStringName)
    {
        var value = tenant.GetConnectionString(connectionStringName) ?? string.Empty;
        return new TenantConnectionStringDto
        {
            Id = tenant.Id,
            TenantId = tenant.Id,
            Name = connectionStringName,
            MaskedValue = _protector.Mask(value),
            CreationTime = tenant.CreationTime
        };
    }
}

public interface ITenantConnectionStringAppService
{
    Task<TenantConnectionStringDto> CreateAsync(string tenantName, CreateTenantConnectionStringDto input, CancellationToken cancellationToken = default);
    Task<TenantConnectionStringDto> UpdateAsync(string tenantName, string connectionStringName, UpdateTenantConnectionStringDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(string tenantName, string connectionStringName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantConnectionStringDto>> GetListAsync(string tenantName, CancellationToken cancellationToken = default);
}
