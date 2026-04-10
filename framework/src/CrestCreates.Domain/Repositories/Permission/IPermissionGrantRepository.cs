using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface IPermissionGrantRepository : ICrestRepositoryBase<PermissionGrant, Guid>
    {
        Task<List<PermissionGrant>> GetListByProviderAsync(
            PermissionGrantProviderType providerType,
            string providerKey,
            CancellationToken cancellationToken = default);

        Task<PermissionGrant?> FindAsync(
            string permissionName,
            PermissionGrantProviderType providerType,
            string providerKey,
            PermissionGrantScope scope,
            string? tenantId,
            CancellationToken cancellationToken = default);

        Task<List<PermissionGrant>> GetListByTenantIdAsync(
            string tenantId,
            CancellationToken cancellationToken = default);
    }
}
