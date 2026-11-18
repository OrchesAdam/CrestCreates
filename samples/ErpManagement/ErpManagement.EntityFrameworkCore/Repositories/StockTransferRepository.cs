using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class StockTransferRepository : EfCoreRepository<StockTransfer, Guid>, IStockTransferRepository
{
    public StockTransferRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public Task<StockTransfer?> GetWithLinesAsync(Guid id, CancellationToken ct = default)
    {
        return GetQueryable().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public Task<List<StockTransfer>> GetAllWithLinesAsync(CancellationToken ct = default)
    {
        return GetQueryable().Include(t => t.Lines).OrderBy(t => t.CreationTime).ToListAsync(ct);
    }
}