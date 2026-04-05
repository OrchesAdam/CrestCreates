using System.Linq;
using CrestCreates.Aop.Abstractions.Interfaces;
using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Aop.Services;

internal class DefaultDataFilterContext : IDataFilterContext
{
    private readonly ICurrentUser? _currentUser;

    public DefaultDataFilterContext(ICurrentUser? currentUser)
    {
        _currentUser = currentUser;
    }

    public string? CurrentTenantId => _currentUser?.TenantId;
    public string? CurrentUserId => _currentUser?.Id;
    public string? CurrentOrganizationId => _currentUser?.OrganizationId?.ToString();
    public bool IsSuperAdmin => _currentUser?.IsSuperAdmin ?? false;

    public IQueryable<TEntity> ApplyTenantFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class
    {
        return query;
    }

    public IQueryable<TEntity> ApplyOrganizationFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class
    {
        return query;
    }

    public IQueryable<TEntity> ApplyDataPermissionFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class
    {
        return query;
    }
}
