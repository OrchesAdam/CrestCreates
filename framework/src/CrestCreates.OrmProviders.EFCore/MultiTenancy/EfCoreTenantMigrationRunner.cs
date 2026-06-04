using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy;

public class EfCoreTenantMigrationRunner : ITenantMigrationRunner
{
    private readonly Func<string, DbContext> _tenantDbContextFactory;
    private readonly ILogger<EfCoreTenantMigrationRunner> _logger;

    public EfCoreTenantMigrationRunner(
        Func<string, DbContext> tenantDbContextFactory,
        ILogger<EfCoreTenantMigrationRunner> logger)
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
