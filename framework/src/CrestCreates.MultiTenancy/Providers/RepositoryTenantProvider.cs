using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy.Providers;

public class RepositoryTenantProvider : ITenantProvider
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RepositoryTenantProvider> _logger;

    public RepositoryTenantProvider(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RepositoryTenantProvider> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<ITenantInfo> GetTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return null!;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenant = await tenantRepository.FindByNameAsync(tenantId.Trim(), cancellationToken);

        if (tenant == null)
        {
            _logger.LogWarning("Tenant not found in repository: {TenantId}", tenantId);
            return null!;
        }

        if (!tenant.IsActive)
        {
            _logger.LogWarning("Tenant is inactive and will not be resolved: {TenantId}", tenantId);
            return null!;
        }

        return new TenantInfo(
            tenant.Name,
            tenant.DisplayName ?? tenant.Name,
            tenant.GetDefaultConnectionString());
    }
}
