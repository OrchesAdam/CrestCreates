using System;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
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
