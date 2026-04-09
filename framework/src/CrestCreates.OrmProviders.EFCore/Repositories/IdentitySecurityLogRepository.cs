using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DataFilter;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class IdentitySecurityLogRepository : EfCoreRepositoryBase<IdentitySecurityLog, Guid>, IIdentitySecurityLogRepository
{
    public IdentitySecurityLogRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public Task<List<IdentitySecurityLog>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(log => log.UserId == userId)
            .OrderByDescending(log => log.CreationTime)
            .ToListAsync(cancellationToken);
    }
}
