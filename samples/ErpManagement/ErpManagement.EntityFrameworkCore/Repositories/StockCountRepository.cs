using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class StockCountRepository : EfCoreRepository<StockCount, Guid>, IStockCountRepository
{
    public StockCountRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public Task<StockCount?> GetWithLinesAsync(Guid id, CancellationToken ct = default)
    {
        return GetQueryable().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public Task<List<StockCount>> GetAllWithLinesAsync(CancellationToken ct = default)
    {
        return GetQueryable().Include(c => c.Lines).OrderBy(c => c.CreationTime).ToListAsync(ct);
    }
}