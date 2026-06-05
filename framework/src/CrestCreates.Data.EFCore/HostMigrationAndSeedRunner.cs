using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.EFCore;

/// <summary>
/// Runs EF Core migrations on all registered DbContexts, then invokes all
/// registered <see cref="IDataSeeder"/> implementations. Designed to be
/// called once during <see cref="ModuleBase.OnApplicationInitialization"/>.
/// </summary>
public class HostMigrationAndSeedRunner
{
    private readonly ILogger<HostMigrationAndSeedRunner> _logger;
    private readonly IReadOnlyList<Type> _dbContextTypes;

    public HostMigrationAndSeedRunner(
        IEnumerable<Type> dbContextTypes,
        ILogger<HostMigrationAndSeedRunner> logger)
    {
        _dbContextTypes = new List<Type>(dbContextTypes);
        _logger = logger;
    }

    public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await RunMigrationsAsync(serviceProvider, cancellationToken);
        await RunDataSeedersAsync(serviceProvider, cancellationToken);
    }

    private async Task RunMigrationsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting host database migrations...");

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var migratedCount = 0;
        foreach (var dbContextType in _dbContextTypes)
        {
            _logger.LogInformation("Migrating {DbContextType}...", dbContextType.Name);

            try
            {
                var dbContext = (DbContext)sp.GetRequiredService(dbContextType);
                await dbContext.Database.MigrateAsync(cancellationToken);
                migratedCount++;
                _logger.LogInformation("Migration complete for {DbContextType}", dbContextType.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed for {DbContextType}: {Message}",
                    dbContextType.Name, ex.Message);
                throw;
            }
        }

        if (migratedCount == 0)
        {
            _logger.LogInformation("No DbContext types registered for migration.");
        }

        _logger.LogInformation("Host database migrations completed ({Count} context(s) migrated).", migratedCount);
    }

    private async Task RunDataSeedersAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting host data seeding...");

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var seeders = sp.GetServices<IDataSeeder>();

        var count = 0;
        foreach (var seeder in seeders)
        {
            _logger.LogInformation("Running data seeder: {SeederType}", seeder.GetType().Name);
            await seeder.SeedAsync(cancellationToken);
            count++;
        }

        _logger.LogInformation("Host data seeding completed ({Count} seeder(s) executed).", count);
    }
}