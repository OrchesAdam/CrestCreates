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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    private readonly ThrowAfterSaveInterceptor _failingInterceptor = new();

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

    private ServiceProvider BuildProvider(string? postgresConnectionString = null, bool includeUnitOfWork = true)
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

            options.AddInterceptors(_failingInterceptor);
            options.LogTo(sql => _sql.Add(sql), LogLevel.Information);
        });
        services.AddScoped<IDataBaseContext>(provider =>
            new RecordingDataBaseContext(
                new EfCoreDbContextAdapter(provider.GetRequiredService<WidgetDbContext>())));
        services.AddScoped<ICrestRepositoryBase<Widget, Guid>>(provider =>
            new EfCoreRepository<Widget, Guid>(provider.GetRequiredService<IDataBaseContext>()));
        services.AddSingleton<IPermissionChecker>(new AllowAllPermissionChecker());
        services.AddSingleton<ICurrentUser>(new CrudEfCurrentUser());
        services.AddSingleton<IDataPermissionFilter>(new IdentityDataPermissionFilter());
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        if (includeUnitOfWork)
        {
            services.AddUnitOfWork();
            services.AddEfCoreUnitOfWork();
        }
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
        var request = BuildRequest();

        _sql.Clear();
        var result = await service.GetListAsync(request);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("alpha", result.Items[0].Name);
        Assert.Contains(_sql, sql =>
            sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("Price", StringComparison.Ordinal) &&
            sql.Contains("CreationTime", StringComparison.Ordinal) &&
            sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
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
        var request = BuildRequest();

        _sql.Clear();
        var result = await service.GetListAsync(request);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("alpha", result.Items[0].Name);
        Assert.Contains(_sql, sql =>
            sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("Price", StringComparison.Ordinal) &&
            sql.Contains("CreationTime", StringComparison.Ordinal) &&
            sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Q01_generated_crud_write_runs_under_the_ef_unit_of_work_with_rollback_zero_residue()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        await context.Database.EnsureCreatedAsync();

        var dataContext = (RecordingDataBaseContext)scope.ServiceProvider.GetRequiredService<IDataBaseContext>();
        var service = scope.ServiceProvider.GetRequiredService<WidgetAppService>();
        var created = await service.CreateAsync(new CreateWidgetDto
        {
            Name = "created",
            Price = 9.5m,
            Description = null,
            Status = WidgetStatus.Draft
        });

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.True(dataContext.ObservedTransaction,
            "the repository must observe the managed EF transaction during the generated write");

        // Throw after the flush has executed inside the transaction; the only way the row can be
        // absent is a real unit-of-work rollback. Without AddUnitOfWork the repository autocommits
        // and this assertion fails.
        _failingInterceptor.Arm();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CreateAsync(new CreateWidgetDto
        {
            Name = "post-save-failure",
            Price = 11.5m,
            Description = null,
            Status = WidgetStatus.Draft
        }));

        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        var stored = await verify.Widgets.FindAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal(1, await verify.Widgets.CountAsync());
        Assert.DoesNotContain(await verify.Widgets.ToListAsync(), widget => widget.Name == "post-save-failure");
    }

    private static WidgetListRequestDto BuildRequest() => new()
    {
        PageIndex = 1,
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

    [Fact]
    public async Task Q01_query_gate_is_sensitive_to_each_filter_and_explicit_sort()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context);
        var service = scope.ServiceProvider.GetRequiredService<WidgetAppService>();

        var baseline = await service.GetListAsync(BuildRequest());
        Assert.Equal(2, baseline.TotalCount);
        Assert.Equal("alpha", baseline.Items[0].Name);

        var withoutPrice = BuildRequest();
        withoutPrice.Filters!.RemoveAll(f => f.Field == "Price");
        var priceResult = await service.GetListAsync(withoutPrice);
        Assert.Equal(3, priceResult.TotalCount);
        Assert.Equal("price-low", priceResult.Items[0].Name);

        var withoutDate = BuildRequest();
        withoutDate.Filters!.RemoveAll(f => f.Field == "CreationTime");
        var dateResult = await service.GetListAsync(withoutDate);
        Assert.Equal(3, dateResult.TotalCount);
        Assert.Equal("date-old", dateResult.Items[0].Name);

        var withoutDescription = BuildRequest();
        withoutDescription.Filters!.RemoveAll(f => f.Field == "Description");
        var descriptionResult = await service.GetListAsync(withoutDescription);
        Assert.Equal(3, descriptionResult.TotalCount);
        Assert.Equal("desc-null", descriptionResult.Items[0].Name);

        var withoutStatus = BuildRequest();
        withoutStatus.Filters!.RemoveAll(f => f.Field == "Status");
        var statusResult = await service.GetListAsync(withoutStatus);
        Assert.Equal(4, statusResult.TotalCount);
        Assert.Equal("status-draft", statusResult.Items[0].Name);

        var withoutSort = BuildRequest();
        withoutSort.Sorts!.Clear();
        var sortResult = await service.GetListAsync(withoutSort);
        Assert.Equal(2, sortResult.TotalCount);
        Assert.Equal("zeta", sortResult.Items[0].Name);
    }

    [Fact]
    public async Task Q01_write_gate_is_sensitive_to_a_missing_unit_of_work()
    {
        await using var provider = BuildProvider(includeUnitOfWork: false);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        await context.Database.EnsureCreatedAsync();
        var dataContext = (RecordingDataBaseContext)scope.ServiceProvider.GetRequiredService<IDataBaseContext>();
        var service = scope.ServiceProvider.GetRequiredService<WidgetAppService>();

        _failingInterceptor.Arm();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CreateAsync(new CreateWidgetDto
        {
            Name = "without-uow",
            Price = 12.5m,
            Description = null,
            Status = WidgetStatus.Draft
        }));

        Assert.False(dataContext.ObservedTransaction,
            "without the unit-of-work assembly the repository must not observe a transaction");
        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        Assert.Contains(await verify.Widgets.ToListAsync(), widget => widget.Name == "without-uow");
    }

    private static async Task SeedAsync(WidgetDbContext context)
    {
        context.Widgets.AddRange(
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "alpha", Price = 10m, CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Description = "keep", Status = WidgetStatus.Draft, ConcurrencyStamp = "s1" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "zeta", Price = 20m, CreationTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Description = "needle-beta", Status = WidgetStatus.Active, ConcurrencyStamp = "s2" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "alpha", Price = 30m, CreationTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Description = "needle-gamma", Status = WidgetStatus.Active, ConcurrencyStamp = "s3" },
            // Each decoy violates exactly one requested filter; removing that filter changes total/page.
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000004"), Name = "price-low", Price = 10m, CreationTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Description = "needle-low", Status = WidgetStatus.Active, ConcurrencyStamp = "s4" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000005"), Name = "date-old", Price = 25m, CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Description = "needle-old", Status = WidgetStatus.Active, ConcurrencyStamp = "s5" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000006"), Name = "desc-null", Price = 35m, CreationTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Description = null, Status = WidgetStatus.Active, ConcurrencyStamp = "s6" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000007"), Name = "status-draft", Price = 45m, CreationTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Description = "needle-draft", Status = WidgetStatus.Draft, ConcurrencyStamp = "s7" },
            new Widget { Id = Guid.Parse("00000000-0000-0000-0000-000000000008"), Name = "retired", Price = 50m, CreationTime = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), Description = "needle-retired", Status = WidgetStatus.Retired, ConcurrencyStamp = "s8" });
        await context.SaveChangesAsync();
    }

    private sealed class RecordingDataBaseContext : IDataBaseContext
    {
        private readonly IDataBaseContext _inner;

        public RecordingDataBaseContext(IDataBaseContext inner)
        {
            _inner = inner;
        }

        public bool ObservedTransaction { get; private set; }


        public IDataBaseTransaction? CurrentTransaction
        {
            get
            {
                var transaction = _inner.CurrentTransaction;
                if (transaction is not null)
                {
                    ObservedTransaction = true;
                }

                return transaction;
            }
        }

        public string? ConnectionString => _inner.ConnectionString;

        public IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class => _inner.Set<TEntity>();

        public Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default) =>
            _inner.SaveChangesAsync(cancellationToken);

        public Task<IDataBaseTransaction> BeginTransactionAsync(System.Threading.CancellationToken cancellationToken = default) =>
            _inner.BeginTransactionAsync(cancellationToken);

        public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class => _inner.Queryable<TEntity>();

        public object GetNativeContext() => _inner.GetNativeContext();

        public void Dispose() => _inner.Dispose();
    }

    private sealed class ThrowAfterSaveInterceptor : SaveChangesInterceptor
    {
        private bool _armed;

        public void Arm() => _armed = true;

        public override int SavedChanges(
            SaveChangesCompletedEventData eventData,
            int result)
        {
            if (_armed)
            {
                _armed = false;
                throw new InvalidOperationException("post-save failure control");
            }

            return base.SavedChanges(eventData, result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (_armed)
            {
                _armed = false;
                return ValueTask.FromException<int>(
                    new InvalidOperationException("post-save failure control"));
            }

            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
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
