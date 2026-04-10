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
    private readonly ITenantBootstrapper _tenantBootstrapper;
    private readonly ILogger<TenantManager> _logger;

    public TenantManager(
        ITenantRepository tenantRepository,
        ITenantBootstrapper tenantBootstrapper,
        ILogger<TenantManager> logger)
    {
        _tenantRepository = tenantRepository;
        _tenantBootstrapper = tenantBootstrapper;
        _logger = logger;
    }

    public async Task<Tenant> CreateAsync(
        string name,
        string? displayName,
        string? defaultConnectionString,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeRequired(name, nameof(name));
        var existingTenant = await _tenantRepository.FindByNameAsync(normalizedName, cancellationToken);
        if (existingTenant is not null)
        {
            throw new InvalidOperationException($"租户 '{normalizedName}' 已存在");
        }

        var tenant = new Tenant(Guid.NewGuid(), normalizedName)
        {
            DisplayName = NormalizeOptional(displayName),
            IsActive = true,
            CreationTime = DateTime.UtcNow
        };

        tenant.SetDefaultConnectionString(defaultConnectionString);
        await _tenantRepository.InsertAsync(tenant, cancellationToken);

        try
        {
            await _tenantBootstrapper.BootstrapAsync(tenant, cancellationToken);
        }
        catch (Exception ex)
        {
            await _tenantRepository.DeleteAsync(tenant, cancellationToken);
            _logger.LogError(ex, "租户 {TenantName} 初始化失败，已自动回滚", tenant.Name);
            throw new InvalidOperationException($"租户 '{tenant.Name}' 创建成功但初始化失败，已自动回滚", ex);
        }

        _logger.LogInformation("租户 {TenantName} (ID: {TenantId}) 创建并初始化完成", tenant.Name, tenant.Id);

        return tenant;
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
