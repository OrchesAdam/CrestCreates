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

public class UserRoleRepository : EfCoreRepositoryBase<UserRole, Guid>, IUserRoleRepository
{
    public UserRoleRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public Task<UserRole?> FindAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .FirstOrDefaultAsync(link => link.UserId == userId && link.RoleId == roleId, cancellationToken);
    }

    public Task<List<UserRole>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(link => link.UserId == userId)
            .OrderBy(link => link.RoleId)
            .ToListAsync(cancellationToken);
    }

    public Task<List<UserRole>> GetByRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(link => link.RoleId == roleId)
            .OrderBy(link => link.UserId)
            .ToListAsync(cancellationToken);
    }
}
