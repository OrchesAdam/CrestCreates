using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.EFCore.MultiTenancy;

public class EfCoreTenantSchemaMigrator : ITenantSchemaMigrator
{
    private readonly Func<string, DbContext> _tenantDbContextFactory;
    private readonly ILogger<EfCoreTenantSchemaMigrator> _logger;

    public EfCoreTenantSchemaMigrator(
        Func<string, DbContext> tenantDbContextFactory,
        ILogger<EfCoreTenantSchemaMigrator> logger)
    {
        _tenantDbContextFactory = tenantDbContextFactory;
        _logger = logger;
    }

    public async Task<TenantMigrationResult> RunAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = _tenantDbContextFactory(context.ConnectionString);
            await dbContext.Database.MigrateAsync(cancellationToken);
            return TenantMigrationResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed for tenant {TenantId}", context.TenantId);
            return TenantMigrationResult.Failed(ex.Message);
        }
    }
}
