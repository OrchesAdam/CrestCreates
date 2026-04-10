using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Application.Tenants;

public interface ITenantDeletionManager
{
    Task<Tenant> ArchiveAsync(string name, CancellationToken cancellationToken = default);
    Task<Tenant> RestoreAsync(string name, CancellationToken cancellationToken = default);
    Task<Tenant> SoftDeleteAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

public class TenantDeletionManager : ITenantDeletionManager
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantDeletionGuard _deletionGuard;
    private readonly TenantDeletionOptions _options;
    private readonly ILogger<TenantDeletionManager> _logger;

    public TenantDeletionManager(
        ITenantRepository tenantRepository,
        ITenantDeletionGuard deletionGuard,
        IOptions<TenantDeletionOptions> options,
        ILogger<TenantDeletionManager> logger)
    {
        _tenantRepository = tenantRepository;
        _deletionGuard = deletionGuard;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Tenant> ArchiveAsync(string name, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{name}' 不存在");

        tenant.Archive();
        tenant.LastModificationTime = DateTime.UtcNow;
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("租户 {TenantName} 已归档", tenant.Name);
        return tenant;
    }

    public async Task<Tenant> RestoreAsync(string name, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{name}' 不存在");

        tenant.Restore();
        tenant.LastModificationTime = DateTime.UtcNow;
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("租户 {TenantName} 已恢复", tenant.Name);
        return tenant;
    }

    public async Task<Tenant> SoftDeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{name}' 不存在");

        var guardResult = await _deletionGuard.CanDeleteAsync(tenant, cancellationToken);
        if (!guardResult.CanDelete)
        {
            throw new InvalidOperationException(guardResult.FailureReason);
        }

        tenant.SoftDelete();
        tenant.LastModificationTime = DateTime.UtcNow;
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("租户 {TenantName} 已软删除", tenant.Name);
        return tenant;
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.FindByNameAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"租户 '{name}' 不存在");

        if (_options.Strategy == TenantDeletionStrategy.Forbidden)
        {
            throw new InvalidOperationException("租户删除已被禁用");
        }

        if (_options.Strategy == TenantDeletionStrategy.Archive)
        {
            await ArchiveAsync(name, cancellationToken);
            return;
        }

        var guardResult = await _deletionGuard.CanDeleteAsync(tenant, cancellationToken);
        if (!guardResult.CanDelete)
        {
            throw new InvalidOperationException(guardResult.FailureReason);
        }

        tenant.SoftDelete();
        tenant.LastModificationTime = DateTime.UtcNow;
        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("租户 {TenantName} 已删除（软删除）", tenant.Name);
    }
}
