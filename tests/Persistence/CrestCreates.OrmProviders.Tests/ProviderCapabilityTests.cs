using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.FreeSql.Extensions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.UnitOfWork;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 切片 3 验收：Provider 能力声明与执行前校验（隔离级别 fail closed、未声明即拒绝）、
/// 按实际数据库/驱动的隔离级别矩阵（SQLite 实测）。
/// </summary>
public class ProviderCapabilityTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "crest-uow-cap-" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        foreach (var suffix in new string[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Explicit_serializable_isolation_is_accepted_on_sqlite_and_commits()
    {
        var provider = BuildEfProvider();
        var entityId = Guid.Empty;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var repository = scope.ServiceProvider.GetRequiredService<EfCoreRepository<CapEntity, Guid>>();

            await using var unitOfWorkScope = manager.BeginScope(new UnitOfWorkOptions
            {
                IsolationLevel = IsolationLevel.Serializable
            });
            await unitOfWorkScope.StartAsync();

            var entity = new CapEntity(Guid.NewGuid(), "serializable");
            entityId = entity.Id;
            await repository.InsertAsync(entity);
            await unitOfWorkScope.CompleteAsync();
        }

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<CapDbContext>();
        (await verifyContext.Entities.FindAsync(entityId)).Should().NotBeNull();
    }

    [Fact]
    public void Explicit_read_committed_is_rejected_before_execution_on_sqlite()
    {
        var provider = BuildEfProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        var act = () => manager.BeginScope(new UnitOfWorkOptions
        {
            IsolationLevel = IsolationLevel.ReadCommitted
        });

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*does not declare support*ReadCommitted*Serializable*");
        manager.CurrentOrNull.Should().BeNull("the gate must reject before creating any unit of work");
    }

    [Fact]
    public void Binding_without_declared_levels_rejects_explicit_isolation_before_creating_the_unit_of_work()
    {
        var factory = new CountingUnitOfWorkFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWorkFactory>(factory);
        services.AddSingleton(new UnitOfWorkProviderBindingRegistry(new[]
        {
            new UnitOfWorkProviderBinding(OrmProvider.FreeSql, sp => new FakeUnitOfWork(), supportsRequiresNew: false)
        }));
        services.AddScoped<IUnitOfWorkManager>(sp => new UnitOfWorkManager(
            sp.GetRequiredService<IUnitOfWorkFactory>(),
            sp.GetRequiredService<UnitOfWorkProviderBindingRegistry>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            explicitDefault: OrmProvider.FreeSql,
            chainNode: null,
            serviceProvider: sp));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        var act = () => manager.BeginScope(new UnitOfWorkOptions
        {
            IsolationLevel = IsolationLevel.ReadCommitted
        });

        act.Should().Throw<NotSupportedException>().WithMessage("*FreeSql*<none declared>*");
        factory.CreateCount.Should().Be(0, "unsupported requests must fail before the provider creates any unit of work");
    }

    [Fact]
    public async Task FreeSql_abandonment_terminates_the_pending_unit_of_work()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddFreeSqlWithUow(
            new FreeSql.FreeSqlBuilder()
                .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=" + _databasePath)
                .Build(),
            repositoryAssembly: null);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await using (var abandoned = manager.BeginScope())
        {
            await abandoned.StartAsync();
            abandoned.State.Should().Be(UnitOfWorkState.Active);
        }

        await using (var next = manager.BeginScope())
        {
            await next.StartAsync();
            next.State.Should().Be(UnitOfWorkState.Active);
        }
    }

    [Fact]
    public void FreeSql_explicit_isolation_is_rejected_by_the_capability_gate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddFreeSqlWithUow(
            new FreeSql.FreeSqlBuilder()
                .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=" + _databasePath)
                .Build(),
            repositoryAssembly: null);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        var act = () => manager.BeginScope(new UnitOfWorkOptions
        {
            IsolationLevel = IsolationLevel.Serializable
        });

        act.Should().Throw<NotSupportedException>().WithMessage("*FreeSql*<none declared>*");
    }

    private ServiceProvider BuildEfProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CapDbContext>(options => options.UseSqlite("Data Source=" + _databasePath));
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<CapDbContext>()));
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddScoped(sp => new EfCoreRepository<CapEntity, Guid>(sp.GetRequiredService<IDataBaseContext>()));
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CapDbContext>().Database.EnsureCreated();
        }

        return provider;
    }

    public sealed class CapDbContext : DbContext
    {
        public CapDbContext(DbContextOptions<CapDbContext> options)
            : base(options)
        {
        }

        public DbSet<CapEntity> Entities => Set<CapEntity>();
    }

    public sealed class CapEntity : AggregateRoot<Guid>
    {
        public CapEntity()
        {
        }

        public CapEntity(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CountingUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public int CreateCount { get; private set; }

        public IUnitOfWork Create(OrmProvider provider)
        {
            CreateCount++;
            return new FakeUnitOfWork();
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public void Dispose()
        {
        }
    }

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;
    }
}
