using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class RoleRepository : EfCoreRepositoryBase<Role, Guid>, IRoleRepository
{
    public RoleRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public Task<Role?> FindByNameAsync(
        string name,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .FirstOrDefaultAsync(role => role.Name == name && role.TenantId == tenantId, cancellationToken);
    }

    public Task<List<Role>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var dbContext = GetNativeDbContext();
        var roles = dbContext.Set<Role>();
        var userRoles = dbContext.Set<UserRole>();

        return roles
            .Join(
                userRoles,
                role => role.Id,
                userRole => userRole.RoleId,
                (role, userRole) => new { role, userRole })
            .Where(joined => joined.userRole.UserId == userId && joined.role.IsActive)
            .Select(joined => joined.role)
            .Distinct()
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);
    }

    private DbContext GetNativeDbContext()
    {
        return (DbContext)GetDbContext().GetNativeContext();
    }
}
