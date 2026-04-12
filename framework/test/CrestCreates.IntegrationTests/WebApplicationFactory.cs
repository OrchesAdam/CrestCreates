using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Services;
using CrestCreates.AuditLogging.Options;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Repositories;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Repositories;
using LibraryManagement.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.IntegrationTests;

public sealed class LibraryManagementWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"librarymanagement-integration-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _sharedConnection;

    public string ConnectionString => $"Data Source={_databasePath}";

    public LibraryManagementWebApplicationFactory()
    {
        _sharedConnection = new SqliteConnection(ConnectionString);
        _sharedConnection.Open();
    }

    public async Task EnsureSeedCompleteAsync()
    {
        var scopeFactory = Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await dbContext.Database.EnsureCreatedAsync();

        // Seed tenant-a user for integration tests
        var tenantA = dbContext.Tenants.FirstOrDefault(t => t.Name == "tenant-a");
        if (tenantA == null)
        {
            tenantA = new Tenant(Guid.NewGuid(), "tenant-a")
            {
                DisplayName = "Tenant A",
                IsActive = true,
                LifecycleState = TenantLifecycleState.Active,
                CreationTime = DateTime.UtcNow
            };
            dbContext.Tenants.Add(tenantA);
            await dbContext.SaveChangesAsync();
        }

        var tenantARole = dbContext.Roles.FirstOrDefault(r => r.Name == "Administrators" && r.TenantId == tenantA.Id.ToString());
        if (tenantARole == null)
        {
            tenantARole = new Role(Guid.NewGuid(), "Administrators", tenantA.Id.ToString())
            {
                DisplayName = "Administrators",
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };
            dbContext.Roles.Add(tenantARole);
            await dbContext.SaveChangesAsync();
        }

        var tenantAUser = dbContext.Users.FirstOrDefault(u => u.UserName == "admin" && u.TenantId == tenantA.Id.ToString());
        if (tenantAUser == null)
        {
            tenantAUser = new User(Guid.NewGuid(), "admin", "admin@tenant-a.local", tenantA.Id.ToString())
            {
                PasswordHash = passwordHasher.HashPassword("Admin123!"),
                IsActive = true,
                IsSuperAdmin = true,
                LockoutEnabled = true,
                CreationTime = DateTime.UtcNow,
                LastPasswordChangeTime = DateTime.UtcNow
            };
            dbContext.Users.Add(tenantAUser);
            await dbContext.SaveChangesAsync();
        }

        await dbContext.SaveChangesAsync();

        if (await dbContext.AuditLogs.AnyAsync())
        {
            return;
        }

        // Use raw SQL to insert audit logs directly - bypasses EF Core tracking issues
        var now = DateTime.UtcNow;
        using var insertCmd = _sharedConnection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO AuditLogs (Id, Duration, UserId, UserName, TenantId, ClientIpAddress, HttpMethod, Url, ServiceName, MethodName, Parameters, ReturnValue, ExceptionMessage, ExceptionStackTrace, Status, ExecutionTime, CreationTime, TraceId, ExtraProperties)
            VALUES
            (@id1, 150, 'host-user-1', 'host-alice', 'host', '192.168.1.1', 'POST', 'https://localhost/api/books', 'BookAppService', 'CreateAsync', NULL, NULL, NULL, NULL, 0, @time1, @now, NULL, '{}'),
            (@id2, 80, 'host-user-2', 'host-bob', 'host', '192.168.1.2', 'GET', 'https://localhost/api/books', 'BookAppService', 'GetListAsync', NULL, NULL, NULL, NULL, 0, @time2, @now, NULL, '{}'),
            (@id3, 200, 'tenant-a-user-1', 'tenant-a-charlie', 'tenant-a', '10.0.0.1', 'POST', 'https://localhost/api/authors', 'AuthorAppService', 'CreateAsync', NULL, NULL, NULL, NULL, 0, @time3, @now, NULL, '{}'),
            (@id4, 60, 'tenant-a-user-2', 'tenant-a-david', 'tenant-a', '10.0.0.2', 'GET', 'https://localhost/api/authors', 'AuthorAppService', 'GetListAsync', NULL, NULL, NULL, NULL, 1, @time4, @now, NULL, '{}')";

        insertCmd.Parameters.AddWithValue("@id1", Guid.NewGuid().ToString());
        insertCmd.Parameters.AddWithValue("@id2", Guid.NewGuid().ToString());
        insertCmd.Parameters.AddWithValue("@id3", Guid.NewGuid().ToString());
        insertCmd.Parameters.AddWithValue("@id4", Guid.NewGuid().ToString());
        insertCmd.Parameters.AddWithValue("@time1", now.AddMinutes(-30));
        insertCmd.Parameters.AddWithValue("@time2", now.AddMinutes(-20));
        insertCmd.Parameters.AddWithValue("@time3", now.AddMinutes(-25));
        insertCmd.Parameters.AddWithValue("@time4", now.AddMinutes(-15));
        insertCmd.Parameters.AddWithValue("@now", now);

        await insertCmd.ExecuteNonQueryAsync();
    }

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
            services.RemoveAll<DbContextOptions<CrestCreatesDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CrestCreatesDbContext>>();
            services.RemoveAll<CrestCreatesDbContext>();
            services.RemoveAll<CrestCreatesDbContextFactory>();
            services.RemoveAll<DbContext>();
            services.RemoveAll<AuditLoggingMiddleware>();
            services.RemoveAll<IAuditLogService>();

            services.AddDbContext<LibraryDbContext>(options =>
            {
                options.UseSqlite(_sharedConnection);
            });

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<LibraryDbContext>());

            services.RemoveAll<IEntityFrameworkCoreDbContext>();
            services.RemoveAll<IDataBaseContext>();
            services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
                new EfCoreDbContextAdapter(sp.GetRequiredService<LibraryDbContext>()));
            services.AddScoped<IDataBaseContext>(sp =>
                sp.GetRequiredService<IEntityFrameworkCoreDbContext>());

            services.RemoveAll<IAuditLogRepository>();
            services.AddScoped<IAuditLogRepository>(sp =>
                new AuditLogRepository(
                    sp.GetRequiredService<IEntityFrameworkCoreDbContext>(),
                    sp.GetRequiredService<ICurrentTenant>()));

            services.AddScoped<AuditLoggingMiddleware>();
            services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
            services.AddScoped<IAuditLogWriter, AuditLogWriter>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IAuditLogAppService, AuditLogAppService>();
            services.AddScoped<IAuditLogCleanupAppService, AuditLogCleanupAppService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        _sharedConnection.Dispose();

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
