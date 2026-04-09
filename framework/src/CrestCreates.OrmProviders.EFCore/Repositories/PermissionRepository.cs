using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using PermissionEntity = CrestCreates.Domain.Permission.Permission;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class PermissionRepository : EfCoreRepositoryBase<PermissionEntity, Guid>, IPermissionRepository
{
    public PermissionRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public async Task<List<PermissionEntity>> GetByRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var dbContext = GetNativeDbContext();
        var roles = dbContext.Set<Role>();
        var permissionGrants = dbContext.Set<PermissionGrant>();
        var permissions = dbContext.Set<PermissionEntity>();

        var roleName = await roles
            .Where(role => role.Id == roleId)
            .Select(role => role.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(roleName))
        {
            return new List<PermissionEntity>();
        }

        var permissionNames = permissionGrants
            .Where(grant =>
                grant.ProviderType == PermissionGrantProviderType.Role &&
                grant.ProviderKey == roleName)
            .Select(grant => grant.PermissionName)
            .Distinct();

        return await permissions
            .Where(permission => permissionNames.Contains(permission.Name))
            .OrderBy(permission => permission.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PermissionEntity>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var dbContext = GetNativeDbContext();
        var roles = dbContext.Set<Role>();
        var userRoles = dbContext.Set<UserRole>();
        var permissionGrants = dbContext.Set<PermissionGrant>();
        var permissions = dbContext.Set<PermissionEntity>();
        var userIdString = userId.ToString();

        var roleNames = roles
            .Join(
                userRoles,
                role => role.Id,
                userRole => userRole.RoleId,
                (role, userRole) => new { role, userRole })
            .Where(joined => joined.userRole.UserId == userId && joined.role.IsActive)
            .Select(joined => joined.role.Name);

        var directPermissionNames = permissionGrants
            .Where(grant =>
                grant.ProviderType == PermissionGrantProviderType.User &&
                grant.ProviderKey == userIdString)
            .Select(grant => grant.PermissionName);

        var rolePermissionNames = permissionGrants
            .Where(grant =>
                grant.ProviderType == PermissionGrantProviderType.Role &&
                roleNames.Contains(grant.ProviderKey))
            .Select(grant => grant.PermissionName);

        var permissionNames = directPermissionNames
            .Union(rolePermissionNames)
            .Distinct();

        return await permissions
            .Where(permission => permissionNames.Contains(permission.Name))
            .OrderBy(permission => permission.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<PermissionEntity?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .FirstOrDefaultAsync(permission => permission.Name == name, cancellationToken);
    }

    private DbContext GetNativeDbContext()
    {
        return (DbContext)GetDbContext().GetNativeContext();
    }
}
