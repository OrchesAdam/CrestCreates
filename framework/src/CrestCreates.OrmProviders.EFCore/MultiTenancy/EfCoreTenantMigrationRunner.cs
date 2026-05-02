using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy;

public class EfCoreTenantMigrationRunner : ITenantMigrationRunner
{
    private readonly ILogger<EfCoreTenantMigrationRunner> _logger;

    public EfCoreTenantMigrationRunner(
        ILogger<EfCoreTenantMigrationRunner> logger)
    {
        _logger = logger;
    }

    public async Task<TenantMigrationResult> RunAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<CrestCreatesDbContext>();
            optionsBuilder.UseSqlServer(context.ConnectionString);

            await using var dbContext = new CrestCreatesDbContext(optionsBuilder.Options);
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
