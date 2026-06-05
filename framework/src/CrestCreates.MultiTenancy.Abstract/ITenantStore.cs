using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantStore
{
    Task<TenantConfiguration?> FindAsync(string tenantIdOrName, CancellationToken cancellationToken = default);
    Task<TenantConfiguration?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantConfiguration>> GetListAsync(CancellationToken cancellationToken = default);
}
