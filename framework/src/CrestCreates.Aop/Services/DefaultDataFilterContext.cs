using System.Linq;
using CrestCreates.Aop.Abstractions.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Aop.Services;

internal class DefaultDataFilterContext : IDataFilterContext
{
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;

    public DefaultDataFilterContext(ICurrentUser? currentUser, ICurrentTenant? currentTenant)
    {
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public string? CurrentTenantId => _currentTenant?.Id ?? _currentUser?.TenantId;
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
