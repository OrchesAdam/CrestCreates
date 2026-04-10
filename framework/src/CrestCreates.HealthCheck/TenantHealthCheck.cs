using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.HealthCheck;

public class TenantHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public TenantHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();

            var tenants = await tenantRepository.GetListWithDetailsAsync(cancellationToken);
            data["TotalTenantCount"] = tenants.Count;

            var activeTenants = tenants.Count(t => t.IsActive && t.LifecycleState == TenantLifecycleState.Active);
            var archivedTenants = tenants.Count(t => t.LifecycleState == TenantLifecycleState.Archived);
            var deletedTenants = tenants.Count(t => t.LifecycleState == TenantLifecycleState.Deleted);

            data["ActiveTenants"] = activeTenants;
            data["ArchivedTenants"] = archivedTenants;
            data["DeletedTenants"] = deletedTenants;

            var tenantsWithoutConnectionString = tenants.FindAll(t => !t.ConnectionStrings.Exists(cs => cs.Name == TenantConnectionString.DefaultName));
            if (tenantsWithoutConnectionString.Count > 0)
            {
                data["TenantsWithoutConnectionString"] = tenantsWithoutConnectionString.ConvertAll(t => t.Name);
            }

            var inactiveTenants = tenants.FindAll(t => !t.IsActive && t.LifecycleState == TenantLifecycleState.Active);
            if (inactiveTenants.Count > 0)
            {
                data["InactiveTenants"] = inactiveTenants.ConvertAll(t => t.Name);
            }

            var tenantsWithIssues = new List<string>();

            foreach (var tenant in tenants.Where(t => t.LifecycleState == TenantLifecycleState.Active && t.IsActive))
            {
                if (!tenant.ConnectionStrings.Exists(cs => cs.Name == TenantConnectionString.DefaultName && !string.IsNullOrWhiteSpace(cs.Value)))
                {
                    tenantsWithIssues.Add($"{tenant.Name}: 缺少有效连接串");
                }
            }

            if (tenantsWithIssues.Count > 0)
            {
                data["TenantsWithIssues"] = tenantsWithIssues;
                return HealthCheckResult.Degraded("部分租户存在配置问题", data: data);
            }

            return HealthCheckResult.Healthy("所有活跃租户配置正常", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("租户健康检查失败", ex, data);
        }
    }
}
