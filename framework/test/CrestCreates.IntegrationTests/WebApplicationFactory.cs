using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using LibraryManagement.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.IntegrationTests;

public sealed class LibraryManagementWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("crestcreates_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly string _schemaName = $"itest_{Guid.NewGuid():N}";
    private string _baseConnectionString = null!;
    private NpgsqlConnection _sharedConnection = null!;
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seedCompleted;

    public string ConnectionString => $"{_baseConnectionString};Search Path={_schemaName}";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _baseConnectionString = _postgres.GetConnectionString();
        EnsureSchemaCreated();
        _sharedConnection = new NpgsqlConnection(ConnectionString);
        await _sharedConnection.OpenAsync();
    }

    private void EnsureSchemaCreated()
    {
        using var connection = new NpgsqlConnection(_baseConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{_schemaName}";""";
        command.ExecuteNonQuery();
    }

    private static async Task EnsureOpenIddictSchemaAsync(OpenIddictDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        // Recreate the OpenIddict schema on every seed run so stale/manual schemas
        // from previous test runs cannot survive package/model upgrades.
        command.CommandText = """
            DROP TABLE IF EXISTS "OpenIddictTokens" CASCADE;
            DROP TABLE IF EXISTS "OpenIddictAuthorizations" CASCADE;
            DROP TABLE IF EXISTS "OpenIddictScopes" CASCADE;
            DROP TABLE IF EXISTS "OpenIddictApplications" CASCADE;
            """;
        await command.ExecuteNonQueryAsync();

        var databaseCreator = dbContext.Database.GetService<IRelationalDatabaseCreator>();
        await databaseCreator.CreateTablesAsync();
    }

    public async Task EnsureSeedCompleteAsync()
    {
        if (_seedCompleted)
        {
            return;
        }

        await _seedLock.WaitAsync();
        try
        {
            if (_seedCompleted)
            {
                return;
            }

            var scopeFactory = Services.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var openIddictDbContext = scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            await dbContext.Database.EnsureCreatedAsync();
            await EnsureOpenIddictSchemaAsync(openIddictDbContext);

            await EnsureOpenIddictClientAsync(applicationManager);

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

            if (!await dbContext.AuditLogs.AnyAsync())
            {
                // Use raw SQL to insert audit logs directly - bypasses EF Core tracking issues
                var now = DateTime.UtcNow;
                using var insertCmd = _sharedConnection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO ""AuditLogs"" (""Id"", ""Duration"", ""UserId"", ""UserName"", ""TenantId"", ""ClientIpAddress"", ""HttpMethod"", ""Url"", ""ServiceName"", ""MethodName"", ""Parameters"", ""ReturnValue"", ""ExceptionMessage"", ""ExceptionStackTrace"", ""Status"", ""ExecutionTime"", ""CreationTime"", ""TraceId"", ""ExtraProperties"")
                    VALUES
                    (@id1, 150, 'host-user-1', 'host-alice', 'host', '192.168.1.1', 'POST', 'https://localhost/api/books', 'BookAppService', 'CreateAsync', NULL, NULL, NULL, NULL, 0, @time1, @now, NULL, '{}'),
                    (@id2, 80, 'host-user-2', 'host-bob', 'host', '192.168.1.2', 'GET', 'https://localhost/api/books', 'BookAppService', 'GetListAsync', NULL, NULL, NULL, NULL, 0, @time2, @now, NULL, '{}'),
                    (@id3, 200, 'tenant-a-user-1', 'tenant-a-charlie', 'tenant-a', '10.0.0.1', 'POST', 'https://localhost/api/authors', 'AuthorAppService', 'CreateAsync', NULL, NULL, NULL, NULL, 0, @time3, @now, NULL, '{}'),
                    (@id4, 60, 'tenant-a-user-2', 'tenant-a-david', 'tenant-a', '10.0.0.2', 'GET', 'https://localhost/api/authors', 'AuthorAppService', 'GetListAsync', NULL, NULL, NULL, NULL, 1, @time4, @now, NULL, '{}')";

                insertCmd.Parameters.AddWithValue("@id1", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@id2", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@id3", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@id4", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@time1", now.AddMinutes(-30));
                insertCmd.Parameters.AddWithValue("@time2", now.AddMinutes(-20));
                insertCmd.Parameters.AddWithValue("@time3", now.AddMinutes(-25));
                insertCmd.Parameters.AddWithValue("@time4", now.AddMinutes(-15));
                insertCmd.Parameters.AddWithValue("@now", now);

                await insertCmd.ExecuteNonQueryAsync();
            }

            _seedCompleted = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static async Task EnsureOpenIddictClientAsync(IOpenIddictApplicationManager applicationManager)
    {
        if (await applicationManager.FindByClientIdAsync("test-client") is not null)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "test-client",
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Test Client"
        };

        descriptor.Permissions.UnionWith(new[]
        {
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.Password,
            Permissions.GrantTypes.RefreshToken,
            Permissions.Prefixes.Scope + Scopes.OpenId,
            Permissions.Prefixes.Scope + Scopes.Profile,
            Permissions.Prefixes.Scope + Scopes.Email,
            Permissions.Prefixes.Scope + Scopes.OfflineAccess
        });

        await applicationManager.CreateAsync(descriptor);
    }

    public new HttpClient CreateClient()
    {
        EnsureSeedCompleteAsync().GetAwaiter().GetResult();
        return base.CreateClient();
    }

    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        EnsureSeedCompleteAsync().GetAwaiter().GetResult();
        return base.CreateClient(options);
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
            services.RemoveAll<DbContextOptions<OpenIddictDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OpenIddictDbContext>>();
            services.RemoveAll<OpenIddictDbContext>();
            services.RemoveAll<DbContext>();
            services.RemoveAll<AuditLoggingMiddleware>();
            services.RemoveAll<IAuditLogService>();

            services.AddDbContext<LibraryDbContext>(options =>
            {
                options.UseNpgsql(_sharedConnection);
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

            // Configure OpenIddict to use EntityFrameworkCore for testing
            // Register a dedicated OpenIddict test DbContext
            services.AddDbContext<OpenIddictDbContext>(options =>
            {
                options.UseNpgsql(_sharedConnection);
            });
        });
    }

    public new async Task DisposeAsync()
    {
        _sharedConnection?.Dispose();
        _seedLock.Dispose();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
