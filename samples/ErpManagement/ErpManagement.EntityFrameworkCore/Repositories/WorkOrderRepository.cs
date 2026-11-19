using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class WorkOrderRepository(IDataBaseContext dbContext) : EfCoreRepository<WorkOrder, Guid>(dbContext), IWorkOrderRepository
{
    private IQueryable<WorkOrder> QueryWithLines(Guid tenantId) => GetQueryable()
        .Where(entity => entity.TenantId == tenantId).Include(entity => entity.Materials).Include(entity => entity.Reports);

    public Task<WorkOrder?> GetWithLinesAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => QueryWithLines(tenantId).FirstOrDefaultAsync(entity => entity.Id == id, ct);

    public async Task<WorkOrder?> GetWithLinesForUpdateAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var database = GetNativeDbContext().Database;
        if (database.CurrentTransaction is null)
            throw new InvalidOperationException("Reporting requires an active transaction.");

        // Read the reports in a fresh statement after the lock is acquired so a waiting retry sees the committed result.
        var locked = await database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM "WorkOrders"
            WHERE "TenantId" = {tenantId} AND "Id" = {id} FOR NO KEY UPDATE
            """).ToListAsync(ct);
        return locked.Count == 0 ? null : await GetWithLinesAsync(tenantId, id, ct);
    }

    public Task<List<WorkOrder>> GetAllWithLinesAsync(Guid tenantId, CancellationToken ct = default)
        => QueryWithLines(tenantId).OrderByDescending(entity => entity.CreationTime).ToListAsync(ct);
}
