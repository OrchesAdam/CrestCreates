using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Services;
using LibraryManagement.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.IntegrationTests;

public sealed class LibraryManagementWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"librarymanagement-integration-{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={_databasePath}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["SeedIdentity:TenantId"] = "host",
                ["SeedIdentity:RoleName"] = "Administrators",
                ["SeedIdentity:UserName"] = "admin",
                ["SeedIdentity:Email"] = "admin@library.local",
                ["SeedIdentity:Password"] = "Admin123!",
                ["CrestLogging:EnableFile"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LibraryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LibraryDbContext>>();
            services.RemoveAll<LibraryDbContext>();
            services.RemoveAll<AuditLoggingMiddleware>();
            services.RemoveAll<IAuditLogService>();

            services.AddDbContext<LibraryDbContext>(options =>
            {
                options.UseSqlite(ConnectionString);
            });

            services.AddScoped<AuditLoggingMiddleware>();
            services.AddScoped<IAuditLogService, AuditLogService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        if (File.Exists(_databasePath))
        {
            DeleteDatabaseFile();
        }
    }

    private void DeleteDatabaseFile()
    {
        const int MaxAttempts = 5;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                SqliteConnection.ClearAllPools();

                if (!File.Exists(_databasePath))
                {
                    return;
                }

                File.Delete(_databasePath);
                return;
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                WaitForFileRelease();
            }
            catch (UnauthorizedAccessException) when (attempt < MaxAttempts)
            {
                WaitForFileRelease();
            }
        }
    }

    private static void WaitForFileRelease()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(200);
    }
}
