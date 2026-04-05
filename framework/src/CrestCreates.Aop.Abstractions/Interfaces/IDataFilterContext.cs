using System.Linq;

namespace CrestCreates.Aop.Abstractions.Interfaces;

public interface IDataFilterContext
{
    string? CurrentTenantId { get; }
    string? CurrentUserId { get; }
    string? CurrentOrganizationId { get; }
    bool IsSuperAdmin { get; }
    
    IQueryable<TEntity> ApplyTenantFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class;
    IQueryable<TEntity> ApplyOrganizationFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class;
    IQueryable<TEntity> ApplyDataPermissionFilter<TEntity>(IQueryable<TEntity> query) where TEntity : class;
}
