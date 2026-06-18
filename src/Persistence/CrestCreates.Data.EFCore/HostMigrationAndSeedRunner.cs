using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
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

    public virtual async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
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
                await MigrateOrEnsureCreatedAsync(dbContext, cancellationToken);
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

    /// <summary>
    /// Applies pending migrations to the database. If no migrations are configured
    /// for the DbContext, uses <see cref="IRelationalDatabaseCreator"/> to ensure
    /// the database exists and create tables from the current model.
    /// </summary>
    /// <remarks>
    /// <para>Multiple DbContexts commonly share the same physical database (e.g.
    /// <c>AppDbContext</c> for business entities and <c>OpenIddictDbContext</c> for
    /// OpenIddict tables). <c>EnsureCreatedAsync</c> works only for the first call —
    /// subsequent calls see existing tables and skip. Using
    /// <see cref="IRelationalDatabaseCreator"/> directly gives us fine-grained control:
    /// ensure the database exists once, then create tables for each DbContext independently.
    /// "Table already exists" errors are treated as a success (idempotent).</para>
    /// </remarks>
    private static async Task MigrateOrEnsureCreatedAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var migrationsAssembly = dbContext.GetService<IMigrationsAssembly>();
        if (migrationsAssembly.Migrations.Count == 0)
        {
            var databaseCreator = dbContext.GetService<IRelationalDatabaseCreator>();

            // Ensure the physical database exists.
            if (!await databaseCreator.ExistsAsync(cancellationToken))
            {
                await databaseCreator.CreateAsync(cancellationToken);
            }

            // CreateTablesAsync throws if tables already exist from a prior run or
            // another DbContext. Treat "already exists" as a success — the schema
            // is already in place and that's exactly what we want.
            try
            {
                await databaseCreator.CreateTablesAsync(cancellationToken);
            }
            catch (Exception ex) when (IsDuplicateTableException(ex))
            {
                // Tables already exist — this is normal on subsequent runs or when
                // multiple DbContexts share the same database.
            }
        }
        else
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Detects whether an exception thrown by <c>CreateTablesAsync</c> is a
    /// "relation/table already exists" error. Provider-agnostic: checks common
    /// patterns across PostgreSQL (42P07), SQL Server (2714), and SQLite (error code 1).
    /// </summary>
    private static bool IsDuplicateTableException(Exception ex)
    {
        // Walk the exception chain — provider exceptions are often wrapped.
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;

            // PostgreSQL: SqlState 42P07 = "relation already exists"
            if (message.Contains("42P07") || (message.Contains("relation") && message.Contains("already exists")))
                return true;

            // SQL Server: error 2714 = "There is already an object named ..."
            if (message.Contains("already an object named") || message.Contains("2714"))
                return true;

            // SQLite: error code 1 = "table already exists"
            if (message.Contains("SQLite Error") && message.Contains("already exists"))
                return true;

            // Generic fallback: any "already exists" message from a known provider.
            if (message.Contains("already exists") &&
                (message.Contains("relation") || message.Contains("table") || message.Contains("object")))
                return true;
        }

        return false;
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