using System.Linq;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Shared.DataFilter
{
    public interface IDataPermissionFilter
    {
        Task<IQueryable<TEntity>> ApplyFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class;
        Task<IQueryable<TEntity>> ApplyTenantFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class;
        Task<IQueryable<TEntity>> ApplyOrganizationFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class;
    }
}
