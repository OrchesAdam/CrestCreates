using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.SqlSugar.Repositories;
using CrestCreates.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

[Collection("ModelFilterRegistry")]
public sealed class ReviewS3R3Probes
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_EF_entries_preserve_detached_creation_fields(bool platformBase)
    {
        using var db = new Issue130SqliteFile();
        var user = new Issue130CurrentUser();
        var tenant = new Issue130MutableTenant(null);
        var options = Issue130EfBed.Options(db, user, tenant);
        await using (var seed = new Issue130EfDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await Issue130EfBed.Repository<Issue130SoftStamped>(seed).InsertAsync(new Issue130SoftStamped(1, "original"));
        }
        var before = db.Read(Issue130Tables.SoftStamped, 1);
        await using var context = new Issue130EfDbContext(options);
        ICrestRepositoryBase<Issue130SoftStamped, long> repo = platformBase
            ? new EfCoreRepositoryBaseProbe(context)
            : Issue130EfBed.Repository<Issue130SoftStamped>(context);
        var detached = new Issue130SoftStamped(1, "updated")
        {
            ConcurrencyStamp = before.Text("ConcurrencyStamp")!,
            CreatorId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            CreationTime = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await repo.UpdateAsync(detached);
        var after = db.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal("updated", after.Text("Name"));
        Assert.Equal(before.GuidValue("CreatorId"), after.GuidValue("CreatorId"));
        Assert.Equal(before.DateTimeValue("CreationTime"), after.DateTimeValue("CreationTime"));
    }

    [Theory]
    [InlineData("tenant-update", false)]
    [InlineData("tenant-update", true)]
    [InlineData("get-id", false)]
    [InlineData("get-id", true)]
    public async Task Sugar_cancellation_after_entry_check_must_precede_SQL(string operation, bool cancel)
    {
        using var db = new Issue130SqliteFile();
        var user = new Issue130CurrentUserProvider();
        var tenant = new Issue130MutableTenant("tenant-a");
        using var sdk = Issue130SqlSugarBed.Create(db, user, tenant);
        var entity = new Issue130TenantEntity(1, "tenant-a", "original");
        await new Issue130SqlSugarRepository<Issue130TenantEntity>(sdk, user, tenant).InsertAsync(entity);
        using var caller = new CancellationTokenSource();
        var statements = new List<string>();
        sdk.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
        var logger = new ReviewS3R3Logger<SqlSugarRepository<Issue130TenantEntity, long>>(
            () => { if (cancel) caller.Cancel(); });
        var repo = new ReviewS3R3SugarRepository(sdk, logger, user, tenant);
        Assert.False(caller.IsCancellationRequested);
        var error = await Record.ExceptionAsync(async () =>
        {
            if (operation == "tenant-update")
            {
                entity.Name = "updated";
                await repo.UpdateAsync(entity, caller.Token);
            }
            else
            {
                Assert.NotNull(await repo.GetAsync(1, caller.Token));
            }
        });
        Assert.True(logger.DebugCalls > 0);
        if (cancel)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Equal("original", db.Read(Issue130Tables.Tenant, 1).Text("Name"));
            Assert.True(statements.Count == 0, "SQL ran after cancellation before command launch: " + string.Join("; ", statements));
        }
        else
        {
            Assert.Null(error);
            Assert.NotEmpty(statements);
            Assert.Equal(operation == "tenant-update" ? "updated" : "original", db.Read(Issue130Tables.Tenant, 1).Text("Name"));
        }
    }
}

public sealed class ReviewS3R3SugarRepository : SqlSugarRepository<Issue130TenantEntity, long>
{
    public ReviewS3R3SugarRepository(SqlSugar.ISqlSugarClient client,
        ILogger<SqlSugarRepository<Issue130TenantEntity, long>> logger,
        ICurrentUserProvider user, CrestCreates.MultiTenancy.Abstract.ICurrentTenant tenant)
        : base(client, logger, user, tenant) { }

    public override IQueryable<Issue130TenantEntity> GetQueryableUnfiltered() => throw new NotSupportedException();
}

public sealed class ReviewS3R3Logger<T> : ILogger<T>
{
    private readonly Action _onDebug;
    public ReviewS3R3Logger(Action onDebug) => _onDebug = onDebug;
    public int DebugCalls { get; private set; }
    public bool IsEnabled(LogLevel level) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    {
        if (level == LogLevel.Debug) { DebugCalls++; _onDebug(); }
    }
}
