using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Generated.CrestCreates_GeneratedCrud_AotFixture_Feature;
using CrestCreates.GeneratedCrud.AotFixture.Domain;
using CrestCreates.GeneratedCrud.AotFixture.Feature.Widgets;
using CrestCreates.GeneratedCrud.AotFixture.Feature.Widgets.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.GeneratedCrud.AotFixture.Tests;

/// <summary>
/// Q01: the formal generated CRUD query adapter executes on a real relational EF Core SQLite
/// database, with decimal/DateTime/nullable/enum filters, sorting/paging and the unique UnitOfWork.
/// The WidgetAppService is the real generated service from the Feature assembly; only the
/// repository/provider infrastructure is EF Core instead of the memory fixture seams.
/// </summary>
public sealed class GeneratedCrudEfCoreRelationalTests : IDisposable
{
    private readonly string _databasePath =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "crest-crud-ef-" + Guid.NewGuid().ToString("N") + ".db");

    private readonly List<string> _sql = new();

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    private ServiceProvider BuildProvider(string? postgresConnectionString = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<WidgetDbContext>(options =>
        {
            if (postgresConnectionString is null)
            {
                options.UseSqlite("Data Source=" + _databasePath);
            }
            else
            {
                options.UseNpgsql(postgresConnectionString);
            }

            options.LogTo(sql => _sql.Add(sql), LogLevel.Information);
        });
        services.AddScoped<IDataBaseContext>(provider =>
            new EfCoreDbContextAdapter(provider.GetRequiredService<WidgetDbContext>()));
        services.AddScoped<ICrestRepositoryBase<Widget, Guid>>(provider =>
            new EfCoreRepository<Widget, Guid>(provider.GetRequiredService<IDataBaseContext>()));
        services.AddSingleton<IPermissionChecker>(new AllowAllPermissionChecker());
        services.AddSingleton<ICurrentUser>(new CrudEfCurrentUser());
        services.AddSingleton<IDataPermissionFilter>(new IdentityDataPermissionFilter());
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();
        services.AddScoped<WidgetAppService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Q01_generated_crud_filters_sort_and_page_execute_on_relational_sqlite()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context);

        var service = scope.ServiceProvider.GetRequiredService<WidgetAppService>();
        var request = new WidgetListRequestDto
        {
            PageIndex = 0,
            PageSize = 1,
            Filters = new List<FilterDescriptor>
            {
                new() { Field = "Price", Operator = FilterOperator.GreaterThan, Value = 15m },
                new() { Field = "CreationTime", Operator = FilterOperator.GreaterThanOrEqual, Value = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
                new() { Field = "Description", Operator = FilterOperator.Contains, Value = "needle" },
                new() { Field = "Status", Operator = FilterOperator.Equals, Value = JsonDocument.Parse("\"Active\"").RootElement }
            },
            Sorts = new List<SortDescriptor>
            {
                new() { Field = "Name", Direction = SortDirection.Descending }
            }
        };

        var result = await service.GetListAsync(request);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("gamma", result.Items[0].Name);
        Assert.Contains(_sql, sql =>
            sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Q01_generated_crud_filters_execute_on_postgresql()
    {
        await using var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await container.StartAsync();

        await using var provider = BuildProvider(container.GetConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context);

        var service = scope.ServiceProvider.GetRequiredService<WidgetAppService>();
        var request = new WidgetListRequestDto
        {
            PageIndex = 0,
            PageSize = 1,
            Filters = new List<FilterDescriptor>
            {
                new() { Field = "Price", Operator = FilterOperator.GreaterThan, Value = 15m },
                new() { Field = "CreationTime", Operator = FilterOperator.GreaterThanOrEqual, Value = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
                new() { Field = "Description", Operator = FilterOperator.Contains, Value = "needle" },
                new() { Field = "Status", Operator = FilterOperator.Equals, Value = JsonDocument.Parse("\"Active\"").RootElement }
            },
            Sorts = new List<SortDescriptor>
            {
                new() { Field = "Name", Direction = SortDirection.Descending }
            }
        };

        var result = await service.GetListAsync(request);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("gamma", result.Items[0].Name);
        Assert.Contains(_sql, sql =>
            sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Q01_generated_crud_write_runs_under_the_ef_unit_of_work()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        await context.Database.EnsureCreatedAsync();

        var service = scope.ServiceProvider.GetRequiredService<WidgetAppService>();
        var created = await service.CreateAsync(new CreateWidgetDto
        {
            Name = "created",
            Price = 9.5m,
            Description = null,
            Status = WidgetStatus.Draft
        });

        Assert.NotEqual(Guid.Empty, created.Id);
        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        var stored = await verify.Widgets.FindAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal("created", stored!.Name);
        Assert.Equal(9.5m, stored.Price);
    }

    private static async Task SeedAsync(WidgetDbContext context)
    {
        context.Widgets.AddRange(
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "alpha", Price = 10m, CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Description = "keep", Status = WidgetStatus.Draft, ConcurrencyStamp = "s1" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "beta", Price = 20m, CreationTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Description = "needle-beta", Status = WidgetStatus.Active, ConcurrencyStamp = "s2" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "gamma", Price = 30m, CreationTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Description = "needle-gamma", Status = WidgetStatus.Active, ConcurrencyStamp = "s3" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000004"), Name = "delta", Price = 40m, CreationTime = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc), Description = null, Status = WidgetStatus.Active, ConcurrencyStamp = "s4" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000005"), Name = "epsilon", Price = 50m, CreationTime = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), Description = "needle-epsilon", Status = WidgetStatus.Retired, ConcurrencyStamp = "s5" });
        await context.SaveChangesAsync();
    }

    public sealed class WidgetDbContext : DbContext
    {
        public WidgetDbContext(DbContextOptions<WidgetDbContext> options) : base(options) { }

        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Widget>(entity =>
            {
                entity.ToTable("Widgets");
                entity.HasKey(e => e.Id);
            });
        }
    }

    private sealed class AllowAllPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);
        public Task<bool> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string permissionName) => Task.FromResult(true);
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames) => Task.FromResult(new MultiplePermissionGrantResult(permissionNames.ToDictionary(name => name, _ => true)));
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string[] permissionNames) => IsGrantedAsync(permissionNames);
        public Task CheckAsync(string permissionName) => Task.CompletedTask;
    }

    private sealed class CrudEfCurrentUser : ICurrentUser
    {
        public string Id { get; } = "11111111-1111-1111-1111-111111111111";
        public string UserName => "q01";
        public bool IsAuthenticated => true;
        public string TenantId => "tenant-a";
        public string[] Roles => Array.Empty<string>();
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => Array.Empty<string>();
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }

    private sealed class IdentityDataPermissionFilter : IDataPermissionFilter
    {
        public Task<IQueryable<TEntity>> ApplyFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class => Task.FromResult(query);
        public Task<IQueryable<TEntity>> ApplyTenantFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class => Task.FromResult(query);
        public Task<IQueryable<TEntity>> ApplyOrganizationFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class => Task.FromResult(query);
    }

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishAsync<TEvent>(TEvent domainEvent, System.Threading.CancellationToken cancellationToken = default) where TEvent : IDomainEvent => Task.CompletedTask;
    }
}
