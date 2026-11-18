using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class BomRepository : EfCoreRepository<Bom, Guid>, IBomRepository
{
    public BomRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public Task<Bom?> GetWithLinesAsync(Guid id, CancellationToken ct = default)
    {
        return GetQueryable().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public Task<List<Bom>> GetAllWithLinesAsync(CancellationToken ct = default)
    {
        return GetQueryable().Include(b => b.Lines).OrderBy(b => b.CreationTime).ToListAsync(ct);
    }

    public Task<List<Bom>> GetByMaterialWithLinesAsync(Guid materialId, CancellationToken ct = default)
    {
        return GetQueryable().Include(b => b.Lines).Where(b => b.MaterialId == materialId).ToListAsync(ct);
    }
}