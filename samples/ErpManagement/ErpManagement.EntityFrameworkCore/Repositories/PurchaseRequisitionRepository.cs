using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class PurchaseRequisitionRepository : EfCoreRepository<PurchaseRequisition, Guid>, IPurchaseRequisitionRepository
{
    public PurchaseRequisitionRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public Task<PurchaseRequisition?> GetWithLinesAsync(Guid id, CancellationToken ct = default)
    {
        return GetQueryable().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public Task<List<PurchaseRequisition>> GetAllWithLinesAsync(CancellationToken ct = default)
    {
        return GetQueryable().Include(r => r.Lines).OrderBy(r => r.CreationTime).ToListAsync(ct);
    }
}