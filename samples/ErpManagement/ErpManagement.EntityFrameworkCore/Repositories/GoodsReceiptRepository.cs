using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.EFCore.UnitOfWork;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ErpManagement.EntityFrameworkCore.Repositories;

public class GoodsReceiptRepository : EfCoreRepository<GoodsReceipt, Guid>, IGoodsReceiptRepository
{
    private readonly IEfCoreTransactionBorrower _borrower;

    public GoodsReceiptRepository(IDataBaseContext dbContext, IEfCoreTransactionBorrower borrower) : base(dbContext)
    {
        _borrower = borrower;
    }

    public async Task<GoodsReceiptStatus?> LockAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        if (!_borrower.TryBorrowCurrent(out var context, out var transaction) ||
            context is not ErpManagementDbContext ||
            transaction?.GetDbTransaction() is not NpgsqlTransaction { Connection: { } connection } postgresTransaction)
        {
            throw new InvalidOperationException("Locking an ERP receipt requires the active managed ERP PostgreSQL transaction.");
        }

        await using var command = new NpgsqlCommand("""
            SELECT "Status" FROM "GoodsReceipts" WHERE "TenantId" = @tenant AND "Id" = @id FOR UPDATE
            """, connection, postgresTransaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        using var cancellation = UnitOfWorkExecutionContext.CombineWithCurrent(ct, out var effective);
        var status = (string?)await command.ExecuteScalarAsync(effective);
        return status is null ? null : Enum.Parse<GoodsReceiptStatus>(status);
    }

    public async Task<GoodsReceipt?> GetForPostingAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var status = await LockAsync(tenantId, id, ct);
        if (status is null)
            return null;

        var query = GetQueryable().Include(r => r.Lines).Where(r => r.Id == id && r.TenantId == tenantId);
        // A replay must read the committed state even if this EF scope already tracked a draft.
        if (status == GoodsReceiptStatus.Posted)
            query = query.AsNoTracking();

        using var cancellation = UnitOfWorkExecutionContext.CombineWithCurrent(ct, out var effective);
        return await query.SingleOrDefaultAsync(effective);
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