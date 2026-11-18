using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class PurchaseOrderRepository : EfCoreRepository<PurchaseOrder, Guid>, IPurchaseOrderRepository
{
    public PurchaseOrderRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public Task<PurchaseOrder?> GetWithLinesAsync(Guid id, CancellationToken ct = default)
    {
        return GetQueryable().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public Task<List<PurchaseOrder>> GetAllWithLinesAsync(CancellationToken ct = default)
    {
        return GetQueryable().Include(o => o.Lines).OrderBy(o => o.CreationTime).ToListAsync(ct);
    }
}