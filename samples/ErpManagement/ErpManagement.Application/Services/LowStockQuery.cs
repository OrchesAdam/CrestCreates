using CrestCreates.Domain.Repositories;
using ErpManagement.Domain.Entities;

namespace ErpManagement.Application.Services;

internal sealed class LowStockQuery(
    ICrestRepositoryBase<StockBalance, Guid> balances,
    ICrestRepositoryBase<Material, Guid> materials)
{
    public IQueryable<Guid> MaterialIds(Guid? tenantId)
    {
        var stock = balances.GetQueryable();
        if (tenantId is Guid id) stock = stock.Where(balance => balance.TenantId == id);
        return (from balance in stock
                join material in materials.GetQueryable()
                    on new { balance.TenantId, Id = balance.MaterialId } equals new { material.TenantId, material.Id }
                where balance.Quantity < material.SafetyStock
                select material.Id).Distinct();
    }
}
