using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.FreeSql.Extensions;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Data.SqlSugar.Extensions;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

[Collection("ModelFilterRegistry")]
public sealed class ReviewS3Probes
{
    [Theory]
    [InlineData(false, "tracked-control")]
    [InlineData(true, "tracked-control")]
    [InlineData(false, "detached")]
    [InlineData(true, "detached")]
    [InlineData(false, "forged")]
    [InlineData(true, "forged")]
    public async Task EF_ownership_preflight_must_use_persistent_facts(bool platformBase, string mode)
    {
        using var db = new Issue130SqliteFile();
        var tenant = new Issue130MutableTenant("tenant-a");
        var user = new Issue130CurrentUser();
        var options = Issue130EfBed.Options(db, user, tenant);
        await using (var seed = new Issue130EfDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.TenantEntities.Add(new Issue130TenantEntity(1, "tenant-a", "original"));
            await seed.SaveChangesAsync();
        }
        var original = db.Read(Issue130Tables.Tenant, 1);
        await using var context = new Issue130EfDbContext(options);
        ICrestRepositoryBase<Issue130TenantEntity, long> repo = platformBase
            ? new ReviewS3EfBaseRepository(context, tenant)
            : Issue130EfBed.Repository<Issue130TenantEntity>(context, tenant);
        var entity = mode == "detached"
            ? new Issue130TenantEntity(1, "tenant-a", "updated") { ConcurrencyStamp = original.Text("ConcurrencyStamp")! }
            : (await repo.GetAsync(1))!;
        entity.Name = "updated";
        if (mode == "forged") { tenant.Id = "tenant-b"; entity.TenantId = "tenant-b"; }
        var error = await Record.ExceptionAsync(() => repo.UpdateAsync(entity));
        var persisted = db.Read(Issue130Tables.Tenant, 1);
        if (mode == "forged")
        {
            Assert.Equal("tenant-a", persisted.Text("TenantId"));
            Assert.Equal("original", persisted.Text("Name"));
            Assert.IsType<InvalidOperationException>(error);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal("updated", persisted.Text("Name"));
        }
    }

    [Theory]
    [InlineData(false, false, "update")]
    [InlineData(true, false, "update")]
    [InlineData(false, false, "id")]
    [InlineData(true, false, "id")]
    [InlineData(false, false, "predicate")]
    [InlineData(true, false, "predicate")]
    [InlineData(false, true, "update")]
    [InlineData(true, true, "update")]
    [InlineData(false, true, "id")]
    [InlineData(true, true, "id")]
    [InlineData(false, true, "predicate")]
    [InlineData(true, true, "predicate")]
    public async Task Optional_shared_rows_are_host_owned_for_writes(bool sugar, bool host, string operation)
    {
        using var db = new Issue130SqliteFile();
        var tenant = new Issue130MutableTenant(host ? null : "tenant-a");
        var user = new Issue130CurrentUserProvider();
        IDisposable client;
        ICrestRepositoryBase<Issue130OptionalTenantEntity, long> repo;
        if (sugar)
        {
            var sdk = Issue130SqlSugarBed.Create(db, user, tenant); client = sdk;
            repo = new Issue130SqlSugarRepository<Issue130OptionalTenantEntity>(sdk, user, tenant);
        }
        else
        {
            var sdk = Issue130FreeSqlBed.Create(db, user, tenant); client = sdk;
            repo = new Issue130FreeSqlRepository<Issue130OptionalTenantEntity>(new FreeSqlUnitOfWorkManager(sdk), user, tenant);
        }
        using (client)
        {
            db.Execute("INSERT INTO S1_OptionalTenant (Id,TenantId,Name) VALUES (3,NULL,'shared')");
            Assert.Single(await repo.GetListAsync());
            var error = await Record.ExceptionAsync(async () =>
            {
                if (operation == "update")
                {
                    var row = (await repo.GetAsync(3))!;row.Name = "changed";await repo.UpdateAsync(row);
                }
                else if (operation == "id") await repo.DeleteAsync(3);
                else await repo.DeleteRangeAsync(row => row.Id == 3);
            });
            if (host)
            {
                Assert.Null(error);
                Assert.Equal(operation == "update" ? "changed" : null, db.Read("S1_OptionalTenant", 3).Text("Name"));
            }
            else
            {
                Assert.Equal("shared", db.Read("S1_OptionalTenant", 3).Text("Name"));
            }
        }
    }

    private static ServiceProvider Provider(Issue130SqliteFile db, bool sugar, Issue130CurrentUser user)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        if (sugar)
        {
            var config = new SqlSugar.ConnectionConfig { ConnectionString = db.ConnectionString, DbType = SqlSugar.DbType.Sqlite, IsAutoCloseConnection = false };
            using (var setup = new SqlSugar.SqlSugarClient(config)) setup.CodeFirst.InitTables<Issue130SoftStamped>();
            services.AddSqlSugarWithUow(config);
            services.AddScoped<Issue130SqlSugarRepository<Issue130SoftStamped>>();
        }
        else
        {
            var sdk = new FreeSql.FreeSqlBuilder().UseConnectionString(FreeSql.DataType.Sqlite, db.ConnectionString).Build();
            sdk.CodeFirst.SyncStructure<Issue130SoftStamped>();
            services.AddFreeSqlWithUow(sdk, typeof(ReviewS3Probes).Assembly);
            services.AddScoped<Issue130FreeSqlRepository<Issue130SoftStamped>>();
        }
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Formal_provider_registration_must_enable_creation_audit(bool sugar)
    {
        using var db = new Issue130SqliteFile();
        var user = new Issue130CurrentUser();
        await using var provider = Provider(db, sugar, user);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(Guid.Parse(user.Id), scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().GetCurrentUserId());
        if (sugar) await scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>().InsertAsync(new Issue130SoftStamped(1, "audit"));
        else await scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>().InsertAsync(new Issue130SoftStamped(1, "audit"));
        Assert.Equal(Guid.Parse(user.Id), db.Read(Issue130Tables.SoftStamped, 1).GuidValue("CreatorId"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_cancellation_before_repository_entry_must_stop_sql(bool sugar)
    {
        using var db = new Issue130SqliteFile();
        await using var provider = Provider(db, sugar, new Issue130CurrentUser());
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var caller = new CancellationTokenSource();
        await using var uow = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        await uow.StartAsync(caller.Token);
        caller.Cancel();
        Assert.True(uow.ExecutionToken.IsCancellationRequested);
        var error = await Record.ExceptionAsync(async () =>
        {
            if (sugar) await scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>().InsertAsync(new Issue130SoftStamped(1, "after-cancel"));
            else await scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>().InsertAsync(new Issue130SoftStamped(1, "after-cancel"));
        });
        Assert.Equal(0, db.Count(Issue130Tables.SoftStamped));
        Assert.IsAssignableFrom<OperationCanceledException>(error);
    }
}

public sealed class ReviewS3EfBaseRepository : EfCoreRepositoryBase<Issue130TenantEntity, long>
{
    public ReviewS3EfBaseRepository(DbContext context, Issue130MutableTenant tenant)
        : base(new EfCoreDbContextAdapter(context), tenant, new CrestCreates.Domain.DataFilter.DataFilterState()) { }
}
