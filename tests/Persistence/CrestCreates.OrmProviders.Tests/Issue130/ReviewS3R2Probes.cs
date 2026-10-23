using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.FreeSql.Extensions;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Data.SqlSugar.Extensions;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

public sealed class ReviewS3R2Probes
{
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
            services.AddFreeSqlWithUow(sdk, typeof(ReviewS3R2Probes).Assembly);
            services.AddScoped<Issue130FreeSqlRepository<Issue130SoftStamped>>();
        }
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false, "normal")]
    [InlineData(true, "normal")]
    [InlineData(false, "forged-create")]
    [InlineData(true, "forged-create")]
    [InlineData(false, "forged-update")]
    [InlineData(true, "forged-update")]
    public async Task Audit_must_derive_creator_from_identity_and_preserve_creation_fields(bool sugar, string mode)
    {
        using var db = new Issue130SqliteFile();
        var user = new Issue130CurrentUser();
        var forged = Guid.Parse("99999999-9999-9999-9999-999999999999");
        await using var provider = Provider(db, sugar, user);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(Guid.Parse(user.Id), scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().GetCurrentUserId());
        ICrestRepositoryBase<Issue130SoftStamped, long> repo = sugar
            ? scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>()
            : scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>();
        var entity = new Issue130SoftStamped(1, "original");
        if (mode == "forged-create") entity.CreatorId = forged;
        await repo.InsertAsync(entity);
        var before = db.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(Guid.Parse(user.Id), before.GuidValue("CreatorId"));
        if (mode == "forged-update")
        {
            var loaded = (await repo.GetAsync(1))!;
            loaded.Name = "updated";
            loaded.CreatorId = forged;
            loaded.CreationTime = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await repo.UpdateAsync(loaded);
            var after = db.Read(Issue130Tables.SoftStamped, 1);
            Assert.Equal("updated", after.Text("Name"));
            Assert.Multiple(
                () => Assert.Equal(Guid.Parse(user.Id), after.GuidValue("CreatorId")),
                () => Assert.Equal(before.Text("CreationTime"), after.Text("CreationTime")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_audit_snapshot_before_SQL_must_prevent_the_write(bool sugar)
    {
        using var db = new Issue130SqliteFile();
        await using var provider = Provider(db, sugar, new Issue130CurrentUser());
        await using var scope = provider.CreateAsyncScope();
        using var caller = new CancellationTokenSource();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var uow = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        await uow.StartAsync(caller.Token);
        Assert.False(uow.ExecutionToken.IsCancellationRequested);
        var snapshot = new ReviewS3R2CancellingUserProvider(caller);
        ICrestRepositoryBase<Issue130SoftStamped, long> repo = sugar
            ? new Issue130SqlSugarRepository<Issue130SoftStamped>(scope.ServiceProvider.GetRequiredService<SqlSugar.ISqlSugarClient>(), snapshot)
            : new Issue130FreeSqlRepository<Issue130SoftStamped>(scope.ServiceProvider.GetRequiredService<FreeSqlUnitOfWorkManager>(), snapshot);
        var error = await Record.ExceptionAsync(() => repo.InsertAsync(new Issue130SoftStamped(1, "before-sql-cancel")));
        Assert.Equal(1, snapshot.Calls);
        Assert.True(uow.ExecutionToken.IsCancellationRequested);
        Assert.Equal(0, db.Count(Issue130Tables.SoftStamped));
        Assert.NotNull(error);
    }
}

public sealed class ReviewS3R2CancellingUserProvider : ICurrentUserProvider
{
    private readonly CancellationTokenSource _caller;
    public ReviewS3R2CancellingUserProvider(CancellationTokenSource caller) => _caller = caller;
    public int Calls { get; private set; }
    public Guid? GetCurrentUserId()
    {
        Calls++;
        _caller.Cancel();
        return Guid.Parse("11111111-1111-1111-1111-111111111111");
    }
}
