using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class GoodsReceiptRepository : EfCoreRepository<GoodsReceipt, Guid>, IGoodsReceiptRepository
{
    public GoodsReceiptRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public Task<GoodsReceipt?> GetWithLinesAsync(Guid id, CancellationToken ct = default)
    {
        return GetQueryable().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public Task<List<GoodsReceipt>> GetAllWithLinesAsync(CancellationToken ct = default)
    {
        return GetQueryable().Include(r => r.Lines).OrderBy(r => r.CreationTime).ToListAsync(ct);
    }
}