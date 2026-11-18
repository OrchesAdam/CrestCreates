using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class SalesOrderRepository(IDataBaseContext dbContext) : EfCoreRepository<SalesOrder, Guid>(dbContext), ISalesOrderRepository
{
    private IQueryable<SalesOrder> QueryWithLines(Guid tenantId) => GetQueryable()
        .Where(entity => entity.TenantId == tenantId).Include(entity => entity.Lines);

    public Task<SalesOrder?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => QueryWithLines(tenantId).FirstOrDefaultAsync(entity => entity.Id == id, ct);

    public Task<List<SalesOrder>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default)
        => QueryWithLines(tenantId).OrderByDescending(entity => entity.CreationTime).ToListAsync(ct);
}
