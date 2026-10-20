using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy.Middleware;
using CrestCreates.MultiTenancy.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;
using SaaSHelpdesk.EntityFrameworkCore;
using SaaSHelpdesk.EntityFrameworkCore.Modules;
using Testcontainers.PostgreSql;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 H04/H07: two registered tenants with different PostgreSQL schemas. The service graph is
/// composed from the real application-owned persistence module, so the production
/// <c>AddDbContext</c> options factory (current tenant connection string) and the unique unit-of-work
/// kernel are exercised — the shared-connection fixture is only single-tenant evidence.
/// </summary>
public class DualTenantDatabaseIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("helpdesk_dual_tenant")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private string _hostConnection = null!;
    private string _tenantConnection = null!;
    private ServiceProvider _provider = null!;
    private Guid _hostTenantId;
    private Guid _tenantId;
    private Guid _hostCustomerId;
    private Guid _tenantCustomerId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var baseConnection = _postgres.GetConnectionString();
        var hostSchema = "host_" + Guid.NewGuid().ToString("N")[..10];
        var tenantSchema = "tenant_" + Guid.NewGuid().ToString("N")[..10];
        CreateSchema(baseConnection, hostSchema);
        CreateSchema(baseConnection, tenantSchema);
        _hostConnection = baseConnection + ";Search Path=" + hostSchema;
        _tenantConnection = baseConnection + ";Search Path=" + tenantSchema;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _hostConnection,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddScoped<DataFilterState>();
        services.AddScoped<IDomainEventPublisher, NoOpDomainEventPublisher>();
        new EntityFrameworkCoreModule().OnConfigureServices(services);
        services.AddMultiTenancy();
        services.AddTenantResolvers(TenantResolutionStrategy.Header);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<ITenantProvider, RepositoryTenantProvider>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        _hostTenantId = Guid.NewGuid();
        _tenantId = Guid.NewGuid();
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            await db.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
            db.Tenants.Add(new Tenant(_hostTenantId, "host")
            {
                DisplayName = "Host",
                IsActive = true,
                LifecycleState = TenantLifecycleState.Active,
            });
            db.Tenants.Add(new Tenant(_tenantId, "tenant-b")
            {
                DisplayName = "Tenant B",
                IsActive = true,
                LifecycleState = TenantLifecycleState.Active,
            });
            await db.SaveChangesAsync();
            db.TenantConnectionStrings.Add(new TenantConnectionString(
                Guid.NewGuid(), _hostTenantId, TenantConnectionString.DefaultName, _hostConnection));
            db.TenantConnectionStrings.Add(new TenantConnectionString(
                Guid.NewGuid(), _tenantId, TenantConnectionString.DefaultName, _tenantConnection));
            _hostCustomerId = Guid.NewGuid();
            db.Customers.Add(new Customer(_hostCustomerId, "Host Customer", "host@example.com", _hostTenantId));
            await db.SaveChangesAsync();
        }

        _tenantCustomerId = Guid.NewGuid();
        await using var tenantDb = new HelpdeskDbContext(OptionsFor(_tenantConnection));
        await tenantDb.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        tenantDb.Customers.Add(
            new Customer(_tenantCustomerId, "Tenant Customer", "tenant@example.com", _tenantId));
        await tenantDb.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task H04_registered_tenants_select_their_own_schema_and_ticket_writes_reads_are_isolated()
    {
        (await ConnectionForTenantAsync(_hostTenantId)).Should().Contain("host_",
            "the production AddDbContext options factory must select the tenant's connection string");
        (await ConnectionForTenantAsync(_tenantId)).Should().Contain("tenant_");

        await InsertTicketAsync(_tenantId, _tenantCustomerId, "tenant b ticket");
        await InsertTicketAsync(_hostTenantId, _hostCustomerId, "host ticket");

        (await ReadTitlesAsync(_tenantId)).Should().Equal("tenant b ticket");
        (await ReadTitlesAsync(_hostTenantId)).Should().Equal("host ticket");
        (await CountTicketsAsync(_tenantConnection)).Should().Be(1);
        (await CountTicketsAsync(_hostConnection)).Should().Be(1);
    }

    [Fact]
    public async Task H04_header_resolution_must_not_pin_the_host_database_in_the_request_scope()
    {
        await InsertTicketAsync(_hostTenantId, _hostCustomerId, "host-only-secret-ticket");
        await InsertTicketAsync(_tenantId, _tenantCustomerId, "tenant-b-ticket");
        using var requestScope = _provider.CreateScope();
        var services = requestScope.ServiceProvider;
        var currentTenant = services.GetRequiredService<ICurrentTenant>();
        currentTenant.Id.Should().BeNull();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/ticket/all";
        context.Request.Headers["X-Tenant-Id"] = _tenantId.ToString();

        // The production resolver graph is resolved from DI; its directory read must not construct
        // the request-scoped business DbContext before the tenant frame exists.
        var resolver = services.GetRequiredService<ITenantResolver>();
        string? selectedConnection = null;
        List<string>? selectedTitles = null;
        var middleware = new MultiTenancyMiddleware(async _ =>
        {
            currentTenant.Id.Should().Be(_tenantId.ToString());
            currentTenant.Tenant!.ConnectionString.Should().Be(_tenantConnection);
            var db = services.GetRequiredService<HelpdeskDbContext>();
            selectedConnection = db.Database.GetDbConnection().ConnectionString;
            selectedTitles = (await services.GetRequiredService<ICrestRepositoryBase<Ticket, Guid>>()
                .GetListAsync()).Select(ticket => ticket.Title).ToList();
        }, NullLogger<MultiTenancyMiddleware>.Instance);

        await middleware.InvokeAsync(context, currentTenant, resolver);

        currentTenant.Id.Should().BeNull();
        selectedTitles.Should().Equal(new[] { "tenant-b-ticket" },
            "the real header resolver and middleware must not expose the host schema to tenant B; "
            + "selected connection was " + selectedConnection);
        selectedConnection.Should().Be(_tenantConnection);
    }

    [Fact]
    public async Task H04_caller_tenant_context_is_restored_after_the_change_scope()
    {
        using var scope = _provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        using (await currentTenant.ChangeAsync(_tenantId.ToString()))
        {
            currentTenant.Id.Should().Be(_tenantId.ToString());
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            db.Database.GetDbConnection().ConnectionString.Should().Contain("tenant_",
                "service, provider and unit of work must observe the same tenant-selected connection");
        }

        currentTenant.Id.Should().BeNull(
            "the change scope restores the caller's tenant frame instead of leaking it");
    }

    [Fact]
    public async Task H07_committed_frame_persists_and_rolled_back_frame_leaves_no_rows()
    {
        await InsertTicketAsync(_tenantId, _tenantCustomerId, "committed");
        (await CountTicketsAsync(_tenantConnection)).Should().Be(1);

        using var scope = _provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (await currentTenant.ChangeAsync(_tenantId.ToString()))
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var repository = scope.ServiceProvider.GetRequiredService<ICrestRepositoryBase<Ticket, Guid>>();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ExecuteAsync<bool>(async ct =>
                {
                    await repository.InsertAsync(
                        new Ticket(Guid.NewGuid(), "rolled back", "description",
                            TicketPriority.Low, TicketType.Incident, _tenantCustomerId), ct);
                    throw new InvalidOperationException("business failure after the write");
                }));
        }

        (await CountTicketsAsync(_tenantConnection)).Should().Be(1,
            "the unique kernel must roll back the tenant-selected transaction");
        (await CountTicketsAsync(_hostConnection)).Should().Be(0,
            "a tenant write must never leak into the host database");
    }

    private async Task InsertTicketAsync(Guid tenantId, Guid customerId, string title)
    {
        using var scope = _provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (await currentTenant.ChangeAsync(tenantId.ToString()))
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var repository = scope.ServiceProvider.GetRequiredService<ICrestRepositoryBase<Ticket, Guid>>();
            await manager.ExecuteAsync(async ct =>
            {
                currentTenant.Id.Should().Be(tenantId.ToString());
                await repository.InsertAsync(
                    new Ticket(Guid.NewGuid(), title, "description", TicketPriority.Low, TicketType.Incident, customerId),
                    ct);
                return true;
            });
        }

        currentTenant.Id.Should().BeNull();
    }

    private async Task<List<string>> ReadTitlesAsync(Guid tenantId)
    {
        using var scope = _provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (await currentTenant.ChangeAsync(tenantId.ToString()))
        {
            var repository = scope.ServiceProvider.GetRequiredService<ICrestRepositoryBase<Ticket, Guid>>();
            return (await repository.GetListAsync()).Select(ticket => ticket.Title).ToList();
        }
    }

    private async Task<string> ConnectionForTenantAsync(Guid tenantId)
    {
        using var scope = _provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (await currentTenant.ChangeAsync(tenantId.ToString()))
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            return db.Database.GetDbConnection().ConnectionString;
        }
    }

    private static async Task<int> CountTicketsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "Tickets";""";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static void CreateSchema(string connectionString, string schema)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        command.ExecuteNonQuery();
    }

    private static DbContextOptions<HelpdeskDbContext> OptionsFor(string connectionString)
        => new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseNpgsql(connectionString)
            .Options;

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;
    }
}
