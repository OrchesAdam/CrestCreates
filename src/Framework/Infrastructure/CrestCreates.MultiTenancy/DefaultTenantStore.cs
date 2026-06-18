using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.MultiTenancy;

public class DefaultTenantStore : ITenantStore
{
    private readonly ITenantProvider _tenantProvider;

    public DefaultTenantStore(ITenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    public async Task<TenantConfiguration?> FindAsync(string tenantIdOrName, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantProvider.GetTenantAsync(tenantIdOrName, cancellationToken);
        if (tenant is null)
            return null;

        return MapToConfiguration(tenant);
    }

    public async Task<TenantConfiguration?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await FindAsync(tenantId.ToString(), cancellationToken);
    }

    public Task<IReadOnlyList<TenantConfiguration>> GetListAsync(CancellationToken cancellationToken = default)
    {
        // ITenantProvider does not support listing all tenants.
        // This method is provided for ITenantStore compatibility.
        return Task.FromResult<IReadOnlyList<TenantConfiguration>>(Array.Empty<TenantConfiguration>());
    }

    private static TenantConfiguration MapToConfiguration(ITenantInfo tenant)
    {
        return new TenantConfiguration
        {
            Id = Guid.TryParse(tenant.Id, out var id) ? id : Guid.Empty,
            Name = tenant.Name ?? string.Empty,
            ConnectionString = tenant.ConnectionString,
            IsActive = true
        };
    }
}