using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Tenants;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Services;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Security.Abstractions;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.EFCore.Settings;
using CrestCreates.Data.EFCore.UnitOfWork;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Application.Services;
using SaaSHelpdesk.Domain.Repositories;
using SaaSHelpdesk.EntityFrameworkCore;
using SaaSHelpdesk.EntityFrameworkCore.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SaaSHelpdesk.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory for SaaSHelpdesk integration tests.
///
/// Starts a PostgreSQL container, creates a schema-isolated test database,
/// seeds admin identity, OpenIddict client, demo categories, customers, and tickets,
/// and wires all required framework services inline.
/// </summary>
public sealed class HelpdeskWebApplicationFactory
    : WebApplicationFactory<global::Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("helpdesk_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly string _schemaName = $"htest_{Guid.NewGuid():N}";
    private string _baseConnectionString = null!;
    private NpgsqlConnection _sharedConnection = null!;
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seedCompleted;

    internal NpgsqlConnection SharedConnection => _sharedConnection;

    /// <summary>
    /// Full connection string with schema search path for the isolated test schema.
    /// </summary>
    public string ConnectionString => $"{_baseConnectionString};Search Path={_schemaName}";

    // ── IAsyncLifetime ──────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _baseConnectionString = _postgres.GetConnectionString();
        EnsureSchemaCreated();
        _sharedConnection = new NpgsqlConnection(ConnectionString);
        await _sharedConnection.OpenAsync();
    }

    public new async Task DisposeAsync()
    {
        _sharedConnection?.Dispose();
        _seedLock.Dispose();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    // ── Schema & seed helpers ────────────────────────────────────────

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
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
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

    private static async Task EnsureOpenIddictClientAsync(IOpenIddictApplicationManager applicationManager)
    {
        if (await applicationManager.FindByClientIdAsync("test-client") is not null)
            return;

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

    /// <summary>
    /// Ensures the database is seeded with required test data (idempotent).
    /// Called automatically by <c>CreateClient()</c>.
    /// </summary>
    public async Task EnsureSeedCompleteAsync()
    {
        if (_seedCompleted)
            return;

        await _seedLock.WaitAsync();
        try
        {
            if (_seedCompleted)
                return;

            var scopeFactory = Services.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            var openIddictDbContext = scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            await dbContext.Database.EnsureCreatedAsync();
            await EnsureOpenIddictSchemaAsync(openIddictDbContext);
            await EnsureOpenIddictClientAsync(applicationManager);

            // Seed host tenant
            var tenant = dbContext.Tenants.FirstOrDefault(t => t.Name == "host");
            if (tenant == null)
            {
                tenant = new Tenant(Guid.NewGuid(), "host")
                {
                    DisplayName = "Host",
                    IsActive = true,
                    LifecycleState = CrestCreates.Domain.Permission.TenantLifecycleState.Active,
                    CreationTime = DateTime.UtcNow
                };
                dbContext.Tenants.Add(tenant);
                await dbContext.SaveChangesAsync();
            }

            // Seed tenant connection string
            if (!dbContext.TenantConnectionStrings.Any(
                    tcs => tcs.TenantId == tenant.Id && tcs.Name == TenantConnectionString.DefaultName))
            {
                dbContext.TenantConnectionStrings.Add(
                    new TenantConnectionString(Guid.NewGuid(), tenant.Id,
                        TenantConnectionString.DefaultName, ConnectionString));
                await dbContext.SaveChangesAsync();
            }

            // Seed admin role
            var role = dbContext.Roles.FirstOrDefault(
                r => r.Name == "Administrators" && r.TenantId == tenant.Id.ToString());
            if (role == null)
            {
                role = new Role(Guid.NewGuid(), "Administrators", tenant.Id.ToString())
                {
                    DisplayName = "Administrators",
                    IsActive = true,
                    CreationTime = DateTime.UtcNow
                };
                dbContext.Roles.Add(role);
                await dbContext.SaveChangesAsync();
            }

            // Seed admin user
            var user = dbContext.Users.FirstOrDefault(
                u => u.UserName == "admin" && u.TenantId == tenant.Id.ToString());
            if (user == null)
            {
                user = new User(Guid.NewGuid(), "admin", "admin@helpdesk.local", tenant.Id.ToString())
                {
                    PasswordHash = passwordHasher.HashPassword("Admin123!"),
                    IsActive = true,
                    IsSuperAdmin = true,
                    LockoutEnabled = true,
                    CreationTime = DateTime.UtcNow,
                    LastPasswordChangeTime = DateTime.UtcNow
                };
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync();
            }

            // Seed UserRole link
            if (!dbContext.UserRoles.Any(ur => ur.UserId == user.Id && ur.RoleId == role.Id))
            {
                dbContext.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenant.Id.ToString()));
                await dbContext.SaveChangesAsync();
            }

            _seedCompleted = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    // ── Client creation (auto-seeds) ─────────────────────────────────

    public new HttpClient CreateClient()
    {
        EnsureSeedCompleteAsync().GetAwaiter().GetResult();
        return base.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        EnsureSeedCompleteAsync().GetAwaiter().GetResult();
        return base.CreateClient(options);
    }

    // ── Host configuration ───────────────────────────────────────────

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
                ["SeedIdentity:Email"] = "admin@helpdesk.local",
                ["SeedIdentity:Password"] = "Admin123!",
                ["CrestLogging:EnableFile"] = "false",
                ["AuditLogging:IsEnabledForGetRequests"] = "true"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Module system resolves IServiceCollection post-build for OnConfigureServices
            services.AddSingleton<IServiceCollection>(services);

            // Data filter state — enables tenant-filter on queries
            services.AddScoped<DataFilterState>();

            // Replace all DbContexts to use the test PostgreSQL connection
            services.RemoveAll<DbContextOptions<HelpdeskDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HelpdeskDbContext>>();
            services.RemoveAll<HelpdeskDbContext>();
            services.RemoveAll<DbContextOptions<CrestCreatesDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CrestCreatesDbContext>>();
            services.RemoveAll<CrestCreatesDbContext>();
            services.RemoveAll<DbContextOptions<OpenIddictDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OpenIddictDbContext>>();
            services.RemoveAll<OpenIddictDbContext>();
            services.RemoveAll<DbContext>();
            services.RemoveAll<AuditLoggingMiddleware>();
            services.RemoveAll<IAuditLogService>();

            // HelpdeskDbContext with shared connection
            services.AddDbContext<HelpdeskDbContext>(options =>
            {
                options.UseNpgsql(_sharedConnection);
            });

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<HelpdeskDbContext>());

            // ORM abstraction adapters
            services.RemoveAll<IEntityFrameworkCoreDbContext>();
            services.RemoveAll<IDataBaseContext>();
            services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
                new EfCoreDbContextAdapter(sp.GetRequiredService<HelpdeskDbContext>()));
            services.AddScoped<IDataBaseContext>(sp =>
                sp.GetRequiredService<IEntityFrameworkCoreDbContext>());

            // Audit log services
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

            // OpenIddict with EntityFrameworkCore
            services.AddDbContext<OpenIddictDbContext>(options =>
            {
                options.UseNpgsql(_sharedConnection);
            });

            // Generic repository registrations (normally provided by EntityFrameworkCoreModule)
            services.AddScoped(typeof(CrestCreates.Domain.Repositories.IRepository<,>), typeof(DomainRepositoryAdapter<,>));
            services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

            // Unit of Work
            services.AddUnitOfWork(OrmProvider.EfCore);
            services.AddScoped<EfCoreUnitOfWork>(sp => new EfCoreUnitOfWork(
                sp.GetRequiredService<IDataBaseContext>(),
                sp.GetRequiredService<IDomainEventPublisher>()));

            // Framework repositories
            services.AddScoped<IPermissionGrantRepository, PermissionGrantRepository>();
            services.AddScoped<IPermissionRepository, PermissionRepository>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<ITenantRepository, TenantRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserRoleRepository, UserRoleRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IIdentitySecurityLogRepository, IdentitySecurityLogRepository>();

            services.AddSettingManagementEfCore();

            // Sample-specific repositories
            services.AddScoped<ITicketRepository, TicketRepository>();

            // Application services (normally registered by ApplicationModule)
            services.AddScoped<ITicketAppService, TicketAppService>();
            services.AddScoped<ICustomerAppService, CustomerAppService>();
            services.AddScoped<ICategoryAppService, CategoryAppService>();
            services.AddScoped<IKnowledgeBaseAppService, KnowledgeBaseAppService>();
            services.AddScoped<ISLAPolicyAppService, SLAPolicyAppService>();
            services.AddScoped<IDashboardAppService, DashboardAppService>();
            services.AddScoped<IAgentAppService, AgentAppService>();
            services.AddScoped<ICustomerPortalAppService, CustomerPortalAppService>();

            // Tenant infrastructure — no-op stubs for PostgreSQL testing
            services.AddScoped<ITenantDatabaseProvisioner, NoOpTenantDatabaseProvisioner>();
            services.AddScoped<ITenantSchemaMigrator, NoOpTenantSchemaMigrator>();
            services.AddScoped<ITenantInitializationStore, NoOpTenantInitializationStore>();
        });
    }

    // ── No-op tenant infrastructure stubs ───────────────────────────

    private sealed class NoOpTenantDatabaseProvisioner : ITenantDatabaseProvisioner
    {
        public Task<TenantDatabaseInitializeResult> InitializeAsync(
            TenantInitializationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(TenantDatabaseInitializeResult.Succeeded());
    }

    private sealed class NoOpTenantSchemaMigrator : ITenantSchemaMigrator
    {
        public Task<TenantMigrationResult> RunAsync(
            TenantInitializationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(TenantMigrationResult.Succeeded());
    }

    private sealed class NoOpTenantInitializationStore : ITenantInitializationStore
    {
        public Task<TenantInitializationRecord?> TryBeginInitializationAsync(
            Guid tenantId, string correlationId, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantInitializationRecord?>(
                new TenantInitializationRecord(Guid.NewGuid(), tenantId, 1, correlationId));

        public Task<TenantInitializationRecord> ForceBeginInitializationAsync(
            Guid tenantId, string correlationId, string reason, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new TenantInitializationRecord(Guid.NewGuid(), tenantId, 1, correlationId));

        public Task<TenantInitializationRecord?> GetLatestAsync(
            Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantInitializationRecord?>(null);

        public Task UpdateAsync(TenantInitializationRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ForceFailAsync(
            Guid tenantId, string correlationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompleteInitializationAsync(
            Guid tenantId, TenantInitializationRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task FailInitializationAsync(
            Guid tenantId, TenantInitializationRecord record, string sanitizedError, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
