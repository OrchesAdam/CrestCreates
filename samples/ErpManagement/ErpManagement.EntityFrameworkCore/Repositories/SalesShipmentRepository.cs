using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class SalesShipmentRepository(IDataBaseContext dbContext) : EfCoreRepository<SalesShipment, Guid>(dbContext), ISalesShipmentRepository
{
    private IQueryable<SalesShipment> QueryWithLines(Guid tenantId) => GetQueryable()
        .Where(entity => entity.TenantId == tenantId).Include(entity => entity.Lines);

    public Task<SalesShipment?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => QueryWithLines(tenantId).FirstOrDefaultAsync(entity => entity.Id == id, ct);

    public Task<List<SalesShipment>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default)
        => QueryWithLines(tenantId).OrderByDescending(entity => entity.CreationTime).ToListAsync(ct);
}
