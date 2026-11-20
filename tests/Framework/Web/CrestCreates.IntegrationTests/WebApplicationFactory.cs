using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.InMemory;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Security.Abstractions;
using CrestCreates.TestServices;
using ErpManagement.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.IntegrationTests;

public sealed class ErpManagementWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _externalPostgres = ExternalTestServices.RequiredConnectionString(
        "CREST_ERP_TEST_PG_CONNECTION", "PostgreSQL 16");
    private readonly string _externalRabbit = ExternalTestServices.RequiredConnectionString(
        "CREST_ERP_TEST_AMQP_CONNECTION", "RabbitMQ 4");
    private readonly string _rabbitContainer = ExternalTestServices.RequiredContainerName(
        "CREST_ERP_TEST_RABBIT_CONTAINER", "RabbitMQ 4");

    // 每夹具独立 vhost：共享 broker 上固定队列名不得跨夹具串消息（等价原每夹具独立容器语义）。
    private readonly string _virtualHost = $"erp_it_{Guid.NewGuid():N}";

    private string BaseConnectionString => _isolatedConnectionString!;
    private string? _isolatedConnectionString;
    private bool _deleteVirtualHost;
    private bool _disposed;
    private readonly string _schemaName = $"itest_{Guid.NewGuid():N}";
    private Task _seedTask = Task.CompletedTask;

    internal const string AuditTenantName = "tenant-a";
    internal Guid AuditTenantId { get; } = Guid.NewGuid();
    internal Guid HostTenantId { get; private set; }
    internal InMemoryAuditSink AuditSink { get; } = new();
    internal Exception? FailNextErpSaveChanges { get; set; }

    internal string RuntimeSchema => _schemaName + "_runtime";
    public string ConnectionString => new NpgsqlConnectionStringBuilder(BaseConnectionString)
    {
        SearchPath = _schemaName
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        var baseConnection = new NpgsqlConnectionStringBuilder(_externalPostgres);
        if (!baseConnection.Database!.Contains("test", StringComparison.OrdinalIgnoreCase) &&
            Environment.GetEnvironmentVariable("CREST_ERP_ALLOW_EXTERNAL_TEST_DATABASE") != "true")
            throw new InvalidOperationException("External ERP test databases require explicit acknowledgment.");
        try
        {
            // 每夹具独立数据库，避免其他夹具建表影响 EnsureCreated。
            _isolatedConnectionString = await ExternalTestServices.CreateIsolatedDatabaseAsync(_externalPostgres, "erp");
            var rabbitUser = Uri.UnescapeDataString(new Uri(_externalRabbit).UserInfo.Split(':', 2)[0]);
            // CLI 超时也可能已经创建了本夹具的 vhost。
            _deleteVirtualHost = true;
            await TestServiceControl.ExecAsync(_rabbitContainer, "rabbitmqctl", "add_vhost", _virtualHost);
            await TestServiceControl.ExecAsync(
                _rabbitContainer, "rabbitmqctl", "set_permissions", "-p", _virtualHost, rabbitUser, ".*", ".*", ".*");
            await using (var connection = new NpgsqlConnection(BaseConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""CREATE SCHEMA "{_schemaName}";""";
                await command.ExecuteNonQueryAsync();
            }

            _seedTask = SeedAsync();
            await _seedTask;
        }
        catch (Exception failure)
        {
            await ExternalTestServices.CleanupAsync([DisposeAsync], failure);
            throw;
        }
    }

    public Task EnsureSeedCompleteAsync() => _seedTask;

    private async Task SeedAsync()
    {
        // Services starts ERP's HostMigrationAndSeedRunner before fixture-only data is inserted.
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        var hostTenant = await dbContext.Tenants.SingleAsync(tenant => tenant.Name == "host");
        HostTenantId = hostTenant.Id;

        await EnsureOpenIddictClientAsync(scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>());

        // The audit isolation tests need a second, authenticated tenant.
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var now = DateTime.UtcNow;
        dbContext.Tenants.Add(new Tenant(AuditTenantId, AuditTenantName)
        {
            DisplayName = "Audit test tenant",
            IsActive = true,
            LifecycleState = TenantLifecycleState.Active,
            CreationTime = now
        });
        dbContext.Users.Add(new User(Guid.NewGuid(), "admin", "audit-admin@example.test", AuditTenantId.ToString())
        {
            PasswordHash = passwordHasher.HashPassword(config["SeedIdentity:Password"]!),
            IsActive = true,
            IsSuperAdmin = true,
            LockoutEnabled = true,
            CreationTime = now,
            LastPasswordChangeTime = now
        });
        await dbContext.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO "AuditLogs" ("Id", "Duration", "UserId", "UserName", "TenantId", "ClientIpAddress", "HttpMethod", "Url", "ServiceName", "MethodName", "Status", "ExecutionTime", "CreationTime", "ExtraProperties")
            VALUES
                (@id1, 150, 'host-user-1', 'host-alice', @hostTenant, '192.168.1.1', 'POST', '/api/material', 'MaterialAppService', 'CreateAsync', 0, @time1, @now, '{}'),
                (@id2, 80, 'host-user-2', 'host-bob', @hostTenant, '192.168.1.2', 'GET', '/api/material/all', 'MaterialAppService', 'GetAllAsync', 0, @time2, @now, '{}'),
                (@id3, 200, 'tenant-a-user-1', 'tenant-a-charlie', @auditTenant, '10.0.0.1', 'POST', '/api/customer', 'CustomerAppService', 'CreateAsync', 0, @time3, @now, '{}'),
                (@id4, 60, 'tenant-a-user-2', 'tenant-a-david', @auditTenant, '10.0.0.2', 'GET', '/api/customer/all', 'CustomerAppService', 'GetAllAsync', 1, @time4, @now, '{}');
            """;
        insertCmd.Parameters.AddWithValue("hostTenant", HostTenantId.ToString());
        insertCmd.Parameters.AddWithValue("auditTenant", AuditTenantId.ToString());
        insertCmd.Parameters.AddWithValue("id1", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("id2", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("id3", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("id4", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("time1", now.AddMinutes(-30));
        insertCmd.Parameters.AddWithValue("time2", now.AddMinutes(-20));
        insertCmd.Parameters.AddWithValue("time3", now.AddMinutes(-25));
        insertCmd.Parameters.AddWithValue("time4", now.AddMinutes(-15));
        insertCmd.Parameters.AddWithValue("now", now);
        await insertCmd.ExecuteNonQueryAsync();
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

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var rabbit = new Uri(_externalRabbit);
        var credentials = rabbit.UserInfo.Split(':', 2);
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString,
            ["Erp:Runtime:Schema"] = _schemaName + "_runtime",
            ["RabbitMQ:HostName"] = rabbit.Host,
            ["RabbitMQ:Port"] = rabbit.Port.ToString(),
            ["RabbitMQ:UserName"] = Uri.UnescapeDataString(credentials[0]),
            ["RabbitMQ:Password"] = Uri.UnescapeDataString(credentials[1]),
            ["RabbitMQ:VirtualHost"] = _virtualHost
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["SeedIdentity:TenantId"] = "host",
                ["SeedIdentity:RoleName"] = "Administrators",
                ["SeedIdentity:UserName"] = "admin",
                ["SeedIdentity:Email"] = "admin@example.test",
                ["SeedIdentity:Password"] = "Admin123!",
                ["CrestLogging:EnableFile"] = "false",
                ["AuditLogging:IsEnabledForGetRequests"] = "true"
            }));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAuditSink>(AuditSink);
            services.ConfigureDbContext<ErpManagementDbContext>(options => options
                .UseNpgsql(ConnectionString).AddInterceptors(new ErpCommitFaultInterceptor(this)));
            UseTestDatabase<OpenIddictDbContext>(services);

            // ERP Program registers SecurityModule and the approval runtime itself.
            services.RemoveAll<ITenantDatabaseProvisioner>();
            services.RemoveAll<ITenantSchemaMigrator>();
            services.RemoveAll<ITenantInitializationStore>();
            services.AddScoped<ITenantDatabaseProvisioner, NoOpTenantDatabaseProvisioner>();
            services.AddScoped<ITenantSchemaMigrator, NoOpTenantSchemaMigrator>();
            services.AddScoped<ITenantInitializationStore, NoOpTenantInitializationStore>();
        });
    }

    private void UseTestDatabase<TContext>(IServiceCollection services) where TContext : DbContext
    {
        services.RemoveAll<TContext>();
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.AddDbContext<TContext>(options => options.UseNpgsql(ConnectionString));
    }

    public new Task DisposeAsync()
    {
        if (_disposed)
            return Task.CompletedTask;
        _disposed = true;

        var cleanup = new List<Func<Task>>
        {
            () => base.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30))
        };
        if (_isolatedConnectionString is { } connectionString)
            cleanup.Add(() => ExternalTestServices.DropDatabaseAsync(
                _externalPostgres, ExternalTestServices.DatabaseName(connectionString)));
        if (_deleteVirtualHost)
            cleanup.Add(() => TestServiceControl.ExecAsync(_rabbitContainer, "rabbitmqctl", "delete_vhost", _virtualHost));
        return ExternalTestServices.CleanupAsync(cleanup);
    }

    private sealed class ErpCommitFaultInterceptor(ErpManagementWebApplicationFactory factory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (factory.FailNextErpSaveChanges is { } failure && eventData.Context is ErpManagementDbContext)
            {
                factory.FailNextErpSaveChanges = null;
                throw failure;
            }
            return ValueTask.FromResult(result);
        }
    }

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
            Guid tenantId, string correlationId, CancellationToken cancellationToken)
            => Task.FromResult<TenantInitializationRecord?>(
                new TenantInitializationRecord(Guid.NewGuid(), tenantId, 1, correlationId));

        public Task<TenantInitializationRecord?> ForceBeginInitializationAsync(
            Guid tenantId, string correlationId, string reason, CancellationToken cancellationToken)
            => Task.FromResult<TenantInitializationRecord?>(
                new TenantInitializationRecord(Guid.NewGuid(), tenantId, 1, correlationId));

        public Task<TenantInitializationRecord?> GetLatestAsync(
            Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<TenantInitializationRecord?>(null);

        public Task UpdateAsync(TenantInitializationRecord record, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ForceFailAsync(
            Guid tenantId, string correlationId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task CompleteInitializationAsync(
            Guid tenantId, TenantInitializationRecord record, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task FailInitializationAsync(
            Guid tenantId, TenantInitializationRecord record, string sanitizedError, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
