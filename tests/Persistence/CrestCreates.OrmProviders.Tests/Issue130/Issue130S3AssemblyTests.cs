using System;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.FreeSql.Extensions;
using CrestCreates.Data.SqlSugar.Extensions;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

/// <summary>S3 assembly core: scoped client/interface and real transaction participation.</summary>
public sealed class Issue130S3AssemblyTests : IDisposable
{
    private readonly Issue130SqliteFile _database = new();

    public void Dispose() => _database.Dispose();

    private ConnectionConfig Config() => new()
    {
        ConnectionString = _database.ConnectionString,
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = false
    };

    private void EnsureSchema()
    {
        using var setup = new SqlSugarClient(Config());
        setup.CodeFirst.InitTables(typeof(Issue130SoftStamped));
    }

    private ServiceProvider BuildProvider()
    {
        EnsureSchema();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddSqlSugarWithUow(Config());
        services.AddScoped<Issue130SqlSugarRepository<Issue130SoftStamped>>();
        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildFreeSqlProvider(Issue130CurrentUser user)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        var freeSql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, _database.ConnectionString)
            .Build();
        freeSql.CodeFirst.SyncStructure<Issue130SoftStamped>();
        services.AddFreeSqlWithUow(freeSql, repositoryAssembly: null);
        services.AddScoped<Issue130FreeSqlRepository<Issue130SoftStamped>>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A03_Formal_FreeSql_registration_persists_create_update_delete_audit_across_scopes()
    {
        var user = new Issue130CurrentUser { Id = "11111111-1111-1111-1111-111111111111" };
        await using var provider = BuildFreeSqlProvider(user);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>()
                .InsertAsync(new Issue130SoftStamped(1, "audited"));
        }
        Assert.Equal(user.Id, _database.Read(Issue130Tables.SoftStamped, 1).GuidValue("CreatorId")?.ToString("D"));

        user.Id = "22222222-2222-2222-2222-222222222222";
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>();
            var loaded = (await repository.GetAsync(1))!;
            loaded.Name = "updated";
            await repository.UpdateAsync(loaded);
        }
        var updated = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(user.Id, updated.GuidValue("LastModifierId")?.ToString("D"));
        Assert.Equal("11111111-1111-1111-1111-111111111111", updated.GuidValue("CreatorId")?.ToString("D"));

        user.Id = "33333333-3333-3333-3333-333333333333";
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>();
            await repository.DeleteAsync(1);
        }
        var deleted = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(1, deleted.Integer("IsDeleted"));
        Assert.Equal(user.Id, deleted.GuidValue("DeleterId")?.ToString("D"));
    }

    [Fact]
    public void U03_SqlSugar_extension_registers_scoped_client_and_interface_alias()
    {
        using var provider = BuildProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstInterface = firstScope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var firstConcrete = firstScope.ServiceProvider.GetRequiredService<SqlSugarClient>();
        var secondInterface = secondScope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        Assert.Same(firstInterface, firstConcrete);
        Assert.NotSame(firstInterface, secondInterface);
    }

    [Fact]
    public async Task A03_Formal_SqlSugar_registration_persists_create_update_delete_audit_across_scopes()
    {
        EnsureSchema();
        var user = new Issue130CurrentUser { Id = "11111111-1111-1111-1111-111111111111" };
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(user);
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddSqlSugarWithUow(Config());
        services.AddScoped<Issue130SqlSugarRepository<Issue130SoftStamped>>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>()
                .InsertAsync(new Issue130SoftStamped(1, "audited"));
        }
        Assert.Equal(user.Id, _database.Read(Issue130Tables.SoftStamped, 1).GuidValue("CreatorId")?.ToString("D"));

        user.Id = "22222222-2222-2222-2222-222222222222";
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();
            var loaded = (await repository.GetAsync(1))!;
            loaded.Name = "updated";
            await repository.UpdateAsync(loaded);
        }
        var updated = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(user.Id, updated.GuidValue("LastModifierId")?.ToString("D"));
        Assert.Equal("11111111-1111-1111-1111-111111111111", updated.GuidValue("CreatorId")?.ToString("D"));

        user.Id = "33333333-3333-3333-3333-333333333333";
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>()
                .DeleteAsync(1);
        }
        var deleted = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(1, deleted.Integer("IsDeleted"));
        Assert.Equal(user.Id, deleted.GuidValue("DeleterId")?.ToString("D"));
    }

    [Fact]
    public async Task U01_SqlSugar_extension_two_repositories_share_uow_and_abandon_hides_writes()
    {
        await using var provider = BuildProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var first = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();
            var second = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await first.InsertAsync(new Issue130SoftStamped(1, "first"));
            await second.InsertAsync(new Issue130SoftStamped(2, "second"));
        }

        Assert.Equal(0, _database.Count(Issue130Tables.SoftStamped));
    }

    [Fact]
    public async Task U01_SqlSugar_extension_two_repositories_share_uow_and_complete_persists_writes()
    {
        await using var provider = BuildProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var first = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();
            var second = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await first.InsertAsync(new Issue130SoftStamped(1, "first"));
            await second.InsertAsync(new Issue130SoftStamped(2, "second"));
            await unitOfWork.CompleteAsync();
        }

        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped));
    }

    [Fact]
    public async Task U07_SqlSugar_cancellation_does_not_poison_the_connection()
    {
        var client = new SqlSugarClient(Config());
        client.CodeFirst.InitTables(typeof(Issue130SoftStamped));
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(
            client, new Issue130CurrentUserProvider(), new Issue130MutableTenant("tenant-a"));
        await repository.InsertAsync(new Issue130SoftStamped(1, "live"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetListAsync(new System.Threading.CancellationToken(canceled: true)));

        Assert.Single(await repository.GetListAsync());
    }
}
