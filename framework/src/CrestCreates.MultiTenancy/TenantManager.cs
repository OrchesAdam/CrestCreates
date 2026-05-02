using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy;

public class TenantManager : ITenantManager
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<TenantManager> _logger;

    public TenantManager(
        ITenantRepository tenantRepository,
        ILogger<TenantManager> logger)
    {
        _tenantRepository = tenantRepository;
        _logger = logger;
    }

    public Task<Tenant> CreateAsync(
        string name,
        string? displayName,
        string? defaultConnectionString)
    {
        var normalizedName = NormalizeRequired(name, nameof(name));
        var tenant = new Tenant(Guid.NewGuid(), normalizedName)
        {
            DisplayName = NormalizeOptional(displayName),
            IsActive = true,
            CreationTime = DateTime.UtcNow
        };

        tenant.SetDefaultConnectionString(defaultConnectionString);

        return Task.FromResult(tenant);
    }

    public async Task<Tenant> UpdateAsync(
        string name,
        string? displayName,
        string? defaultConnectionString,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeRequired(name, nameof(name));
        var tenant = await _tenantRepository.FindByNameAsync(normalizedName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{normalizedName}' 不存在");

        tenant.DisplayName = NormalizeOptional(displayName);
        tenant.SetDefaultConnectionString(defaultConnectionString);
        tenant.LastModificationTime = DateTime.UtcNow;

        await _tenantRepository.UpdateAsync(tenant, cancellationToken);
        return tenant;
    }

    public async Task SetActiveAsync(
        string name,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeRequired(name, nameof(name));
        var tenant = await _tenantRepository.FindByNameAsync(normalizedName, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{normalizedName}' 不存在");

        tenant.IsActive = isActive;
        tenant.LastModificationTime = DateTime.UtcNow;
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);
    }

    public Task DeleteTenantOnlyAsync(
        Tenant tenant,
        CancellationToken cancellationToken = default)
    {
        return _tenantRepository.DeleteAsync(tenant, cancellationToken);
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
