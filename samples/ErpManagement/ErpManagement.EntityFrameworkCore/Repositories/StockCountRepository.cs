using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Application.Contracts.Interfaces;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class StockCountRepository : EfCoreRepository<StockCount, Guid>, IStockCountRepository
{
    private readonly ICurrentTenant _currentTenant;

    public StockCountRepository(IDataBaseContext dbContext, ICurrentTenant currentTenant) : base(dbContext)
    {
        _currentTenant = currentTenant;
    }

    public override IQueryable<StockCount> GetQueryable()
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var id) ? id : Guid.Empty;
        return base.GetQueryable().Where(c => c.TenantId == tenantId);
    }

    public override Task<StockCount?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => GetQueryable().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<StockCount?> GetWithLinesAsync(Guid id, CancellationToken ct = default)
    {
        return GetQueryable().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public override async Task<StockCount> UpdateAsync(StockCount count, CancellationToken cancellationToken = default)
    {
        var dbContext = GetNativeDbContext();
        // Client-generated Guid keys make navigation fixup mark new lines Modified even before
        // Update() runs. Identify lines absent from the database, detach them for the base update,
        // then re-attach explicitly as Added.
        var newLines = count.Lines
            .Where(line => dbContext.Entry(line).State != EntityState.Unchanged
                           && dbContext.Entry(line).GetDatabaseValues() is null)
            .ToList();
        foreach (var line in newLines)
        {
            count.Lines.Remove(line);
            dbContext.Entry(line).State = EntityState.Detached;
        }
        var result = await base.UpdateAsync(count, cancellationToken);
        foreach (var line in newLines)
        {
            count.Lines.Add(line);
            dbContext.Entry(line).State = EntityState.Added;
        }
        if (newLines.Count > 0 && dbContext.Database.CurrentTransaction is null)
            await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public Task<List<StockCount>> GetAllWithLinesAsync(CancellationToken ct = default)
    {
        return GetQueryable().Include(c => c.Lines).OrderBy(c => c.CreationTime).ToListAsync(ct);
    }
}