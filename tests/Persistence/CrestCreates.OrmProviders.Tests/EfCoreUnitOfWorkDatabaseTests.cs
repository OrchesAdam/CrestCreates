using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.UnitOfWork;
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
/// 唯一装配路径在真实数据库（SQLite 文件库）上的生命周期验证：
/// 提交持久化、回滚丢弃、requiresNew 独立上下文/事务、提交后事件顺序。
/// </summary>
public class EfCoreUnitOfWorkDatabaseTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"crest-uow-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Commit_persists_changes_to_the_database()
    {
        var (provider, _) = BuildProvider();
        Guid entityId;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<UowTestDbContext>();

            using (var unitOfWorkScope = manager.BeginScope())
            {
                await unitOfWorkScope.UnitOfWork.BeginTransactionAsync();
                var entity = new UowTestEntity(Guid.NewGuid(), "committed");
                entityId = entity.Id;
                dbContext.Entities.Add(entity);
                await unitOfWorkScope.UnitOfWork.CommitTransactionAsync();
            }

            manager.CurrentOrNull.Should().BeNull();
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(entityId)).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Rollback_discards_changes_without_residue()
    {
        var (provider, _) = BuildProvider();
        Guid entityId;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<UowTestDbContext>();

            using (var unitOfWorkScope = manager.BeginScope())
            {
                await unitOfWorkScope.UnitOfWork.BeginTransactionAsync();
                var entity = new UowTestEntity(Guid.NewGuid(), "rolled-back");
                entityId = entity.Id;
                dbContext.Entities.Add(entity);
                await dbContext.SaveChangesAsync();
                await unitOfWorkScope.UnitOfWork.RollbackTransactionAsync();
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(entityId)).Should().BeNull();
        }
    }

    [Fact]
    public async Task RequiresNew_provides_independent_context_and_transaction()
    {
        // SQLite file databases serialize write transactions (BEGIN IMMEDIATE),
        // so isolation is proven sequentially: distinct DbContext per child
        // scope, independent commit/rollback outcomes, parent transaction
        // untouched by inner scope disposal, and parent state restoration.
        var (provider, _) = BuildProvider(useTrackingBinding: true);
        Guid innerEntityId;
        Guid outerEntityId;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            using (var outerScope = manager.BeginScope())
            {
                var outerUnitOfWork = (ContextTrackingUnitOfWork)outerScope.UnitOfWork;

                using (var innerScope = manager.BeginScope(requiresNew: true))
                {
                    var innerUnitOfWork = (ContextTrackingUnitOfWork)innerScope.UnitOfWork;
                    innerUnitOfWork.Context.Should().NotBeSameAs(
                        outerUnitOfWork.Context,
                        "requiresNew must isolate the DbContext/connection, not wrap a shared one");

                    await innerUnitOfWork.BeginTransactionAsync();
                    var innerEntity = new UowTestEntity(Guid.NewGuid(), "inner");
                    innerEntityId = innerEntity.Id;
                    innerUnitOfWork.Context.Entities.Add(innerEntity);
                    await innerUnitOfWork.CommitTransactionAsync();
                }

                manager.Current.Should().BeSameAs(outerUnitOfWork,
                    "disposing the inner scope must restore the parent unit of work");

                await outerUnitOfWork.BeginTransactionAsync();
                var outerEntity = new UowTestEntity(Guid.NewGuid(), "outer");
                outerEntityId = outerEntity.Id;
                outerUnitOfWork.Context.Entities.Add(outerEntity);

                using (var secondInnerScope = manager.BeginScope(requiresNew: true))
                {
                    ((ContextTrackingUnitOfWork)secondInnerScope.UnitOfWork).Context.Should().NotBeSameAs(
                        outerUnitOfWork.Context);
                }

                outerUnitOfWork.Context.Database.CurrentTransaction.Should().NotBeNull(
                    "inner scope creation/disposal must not close or release the outer transaction");
                manager.Current.Should().BeSameAs(outerUnitOfWork);

                await outerUnitOfWork.RollbackTransactionAsync();
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(innerEntityId)).Should().NotBeNull(
                "the inner committed transaction must be independent of the outer one");
            (await verifyContext.Entities.FindAsync(outerEntityId)).Should().BeNull(
                "the outer rollback must discard only the outer transaction's changes");
        }
    }

    [Fact]
    public async Task Domain_events_publish_after_the_commit_is_persisted()
    {
        var publisher = new RecordingDomainEventPublisher();
        var (provider, _) = BuildProvider(useTrackingBinding: false, publisher: publisher);

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<UowTestDbContext>();

            publisher.RowVisibilityProbe = async () =>
            {
                using var probeScope = provider.CreateScope();
                var probeContext = probeScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
                return await probeContext.Entities.AnyAsync(entity => entity.Name == "with-event");
            };

            using (var unitOfWorkScope = manager.BeginScope())
            {
                await unitOfWorkScope.UnitOfWork.BeginTransactionAsync();
                var entity = new UowTestEntity(Guid.NewGuid(), "with-event");
                entity.AddDomainEvent(new UowTestDomainEvent { OccurredOn = DateTime.UtcNow });
                dbContext.Entities.Add(entity);
                await unitOfWorkScope.UnitOfWork.CommitTransactionAsync();
            }
        }

        publisher.Published.Should().ContainSingle();
        publisher.RowVisibleDuringPublish.Should().BeTrue(
            "lifecycle/domain events must be published only after the persisted state is committed");
    }

    private (ServiceProvider Provider, RecordingDomainEventPublisher Publisher) BuildProvider(
        bool useTrackingBinding = false,
        RecordingDomainEventPublisher? publisher = null)
    {
        publisher ??= new RecordingDomainEventPublisher();
        var capturedPublisher = publisher;

        var services = new ServiceCollection();
        services.AddDbContext<UowTestDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<UowTestDbContext>());
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<UowTestDbContext>()));
        services.AddScoped<IDataBaseContext>(sp =>
            sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddSingleton<IDomainEventPublisher>(capturedPublisher);
        services.AddUnitOfWork();

        if (useTrackingBinding)
        {
            services.AddScoped<ContextTrackingUnitOfWork>();
            services.AddUnitOfWorkProvider(
                OrmProvider.EfCore,
                static sp => sp.GetRequiredService<ContextTrackingUnitOfWork>());
        }
        else
        {
            services.AddEfCoreUnitOfWork();
        }

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<UowTestDbContext>().Database.EnsureCreated();
        }

        return (provider, publisher);
    }

    public sealed class UowTestDbContext : DbContext
    {
        public UowTestDbContext(DbContextOptions<UowTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<UowTestEntity> Entities => Set<UowTestEntity>();
    }

    public sealed class UowTestEntity : AggregateRoot<Guid>
    {
        public UowTestEntity()
        {
        }

        public UowTestEntity(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class UowTestDomainEvent : IDomainEvent
    {
        public DateTime OccurredOn { get; init; }
    }

    private sealed class ContextTrackingUnitOfWork : IUnitOfWork
    {
        private readonly EfCoreUnitOfWork _inner;

        public ContextTrackingUnitOfWork(UowTestDbContext context, IDomainEventPublisher domainEventPublisher)
        {
            Context = context;
            _inner = new EfCoreUnitOfWork(context, domainEventPublisher);
        }

        public UowTestDbContext Context { get; }

        public Task BeginTransactionAsync() => _inner.BeginTransactionAsync();

        public Task CommitTransactionAsync() => _inner.CommitTransactionAsync();

        public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();

        public Task<int> SaveChangesAsync() => _inner.SaveChangesAsync();

        public void Dispose() => _inner.Dispose();
    }

    private sealed class RecordingDomainEventPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> Published { get; } = new();

        public Func<Task<bool>>? RowVisibilityProbe { get; set; }

        public bool? RowVisibleDuringPublish { get; private set; }

        public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            if (RowVisibilityProbe is not null)
            {
                RowVisibleDuringPublish = await RowVisibilityProbe();
            }

            Published.Add(domainEvent);
        }

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => PublishAsync((IDomainEvent)domainEvent, cancellationToken);
    }
}
