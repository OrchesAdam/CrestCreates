using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class ArApLedgerRepository(IDataBaseContext dbContext) : EfCoreRepository<ArApLedger, Guid>(dbContext), IArApLedgerRepository
{
    private IQueryable<ArApLedger> QueryWithLines(Guid tenantId) => GetQueryable()
        .Where(entity => entity.TenantId == tenantId).Include(entity => entity.Settlements);

    public Task<ArApLedger?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => QueryWithLines(tenantId).FirstOrDefaultAsync(entity => entity.Id == id, ct);

    public Task<List<ArApLedger>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default)
        => QueryWithLines(tenantId).OrderByDescending(entity => entity.CreationTime).ToListAsync(ct);
}
