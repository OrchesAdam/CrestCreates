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

public class UserRepository : EfCoreRepositoryBase<User, Guid>, IUserRepository
{
    public UserRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        var queryable = GetQueryable();
        return queryable.FirstOrDefaultAsync(user => user.UserName == userName, cancellationToken);
    }

    public Task<User?> FindByUserNameAsync(string userName, string tenantId, CancellationToken cancellationToken = default)
    {
        var queryable = GetQueryable();
        return queryable.FirstOrDefaultAsync(
            user => user.UserName == userName && user.TenantId == tenantId, cancellationToken);
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var queryable = GetQueryable();
        return queryable.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    public Task<List<User>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(user => user.OrganizationId == organizationId)
            .OrderBy(user => user.UserName)
            .ToListAsync(cancellationToken);
    }

    public Task<List<User>> GetListByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(user => user.TenantId == tenantId)
            .OrderBy(user => user.UserName)
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetCountByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .CountAsync(user => user.TenantId == tenantId, cancellationToken);
    }
}
