using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy;

public class EfCoreTenantInitializationStore : ITenantInitializationStore
{
    private readonly CrestCreatesDbContext _dbContext;
    private readonly ILogger<EfCoreTenantInitializationStore> _logger;

    public EfCoreTenantInitializationStore(
        CrestCreatesDbContext dbContext,
        ILogger<EfCoreTenantInitializationStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<TenantInitializationRecord?> TryBeginInitializationAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Atomically transition Pending(0) or Failed(3) -> Initializing(1)
            var rows = await _dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE Tenants SET InitializationStatus = {0} WHERE Id = {1} AND InitializationStatus IN (0, 3)",
                new object[] { (int)TenantInitializationStatus.Initializing, tenantId },
                cancellationToken);

            if (rows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            // Detach any tracked Tenant entity so subsequent loads see the updated status
            DetachTrackedTenant(tenantId);

            // Compute next attempt number using the underlying DbSet for LINQ queries
            var dbSet = ((DbContext)_dbContext).Set<TenantInitializationRecord>();
            var maxAttempt = await dbSet
                .Where(r => r.TenantId == tenantId)
                .MaxAsync(r => (int?)r.AttemptNo, cancellationToken) ?? 0;

            var record = new TenantInitializationRecord(
                Guid.NewGuid(), tenantId, maxAttempt + 1, correlationId);

            await _dbContext.Set<TenantInitializationRecord>().AddAsync(record, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return record;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TenantInitializationRecord?> ForceBeginInitializationAsync(
        Guid tenantId,
        string correlationId,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Atomically transition Pending(0), Initializing(1), or Failed(3) -> Initializing(1)
            var rows = await _dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE Tenants SET InitializationStatus = {0} WHERE Id = {1} AND InitializationStatus IN (0, 1, 3)",
                new object[] { (int)TenantInitializationStatus.Initializing, tenantId },
                cancellationToken);

            if (rows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            DetachTrackedTenant(tenantId);

            // Compute next attempt number using the underlying DbSet for LINQ queries
            var dbSet = ((DbContext)_dbContext).Set<TenantInitializationRecord>();
            var maxAttempt = await dbSet
                .Where(r => r.TenantId == tenantId)
                .MaxAsync(r => (int?)r.AttemptNo, cancellationToken) ?? 0;

            var record = new TenantInitializationRecord(
                Guid.NewGuid(), tenantId, maxAttempt + 1, correlationId);
            record.SetCurrentStep(reason);

            await _dbContext.Set<TenantInitializationRecord>().AddAsync(record, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return record;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TenantInitializationRecord?> GetLatestAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Use the underlying DbSet for LINQ queries
        var dbSet = ((DbContext)_dbContext).Set<TenantInitializationRecord>();

        return await dbSet
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.AttemptNo)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        TenantInitializationRecord record,
        CancellationToken cancellationToken)
    {
        _dbContext.Set<TenantInitializationRecord>().Update(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ForceFailAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Atomically transition Initializing(1) → Failed(3)
            var rows = await _dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE Tenants SET InitializationStatus = {0}, LastInitializationError = {1} WHERE Id = {2} AND InitializationStatus = 1",
                new object[] { (int)TenantInitializationStatus.Failed, "manually marked as failed", tenantId },
                cancellationToken);

            if (rows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Cannot force-fail tenant {tenantId}: it is not in Initializing state.");
            }

            DetachTrackedTenant(tenantId);

            // Update the latest Initializing record if it exists
            var dbSet = ((DbContext)_dbContext).Set<TenantInitializationRecord>();
            var activeRecord = await dbSet
                .Where(r => r.TenantId == tenantId && r.Status == TenantInitializationStatus.Initializing)
                .OrderByDescending(r => r.AttemptNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeRecord is not null)
            {
                activeRecord.MarkFailed("manually marked as failed");
                _dbContext.Set<TenantInitializationRecord>().Update(activeRecord);
            }
            else
            {
                // Recovery branch: no active Initializing record
                var maxAttempt = await dbSet
                    .Where(r => r.TenantId == tenantId)
                    .MaxAsync(r => (int?)r.AttemptNo, cancellationToken) ?? 0;

                var recoveryRecord = new TenantInitializationRecord(
                    Guid.NewGuid(), tenantId, maxAttempt + 1, correlationId);
                recoveryRecord.MarkFailed("manually marked as failed");

                await _dbContext.Set<TenantInitializationRecord>().AddAsync(recoveryRecord, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private void DetachTrackedTenant(Guid tenantId)
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<Tenant>())
        {
            if (entry.Entity.Id == tenantId)
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
