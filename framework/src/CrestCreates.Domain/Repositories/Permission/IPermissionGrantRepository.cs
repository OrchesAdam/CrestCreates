using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface IPermissionGrantRepository : ICrestRepositoryBase<PermissionGrant, Guid>
    {
        Task<List<PermissionGrant>> GetListByProviderAsync(
            string providerName, 
            string providerKey, 
            CancellationToken cancellationToken = default);
        
        Task<PermissionGrant?> FindAsync(
            string name, 
            string providerName, 
            string providerKey, 
            CancellationToken cancellationToken = default);
        
        Task<List<PermissionGrant>> GetListByProviderNameAsync(
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default);
    }
}
