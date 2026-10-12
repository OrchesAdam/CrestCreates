using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 唯一装配路径在真实数据库（SQLite 文件库）上的生命周期验证：
/// 提交持久化、回滚丢弃、requiresNew 将预注入业务依赖绑定到内层 UoW、
/// 嵌套内核执行、提交后事件顺序。
/// </summary>
public class EfCoreUnitOfWorkDatabaseTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"crest-uow-{Guid.NewGuid():N}.db");

    private static readonly UnitOfWorkOptions RequiresNewOptions = new()
    {
        Propagation = UnitOfWorkPropagation.RequiresNew
    };

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

            await using (var unitOfWorkScope = manager.BeginScope())
            {
                await unitOfWorkScope.StartAsync();
                var entity = new UowTestEntity(Guid.NewGuid(), "committed");
                entityId = entity.Id;
                dbContext.Entities.Add(entity);
                await unitOfWorkScope.CompleteAsync();
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

            await using (var unitOfWorkScope = manager.BeginScope())
            {
                await unitOfWorkScope.StartAsync();
                var entity = new UowTestEntity(Guid.NewGuid(), "rolled-back");
                entityId = entity.Id;
                dbContext.Entities.Add(entity);
                await dbContext.SaveChangesAsync();
                await unitOfWorkScope.RollbackAsync();
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(entityId)).Should().BeNull();
        }
    }

    [Fact]
    public async Task RequiresNew_binds_preinjected_dependencies_to_the_inner_unit_of_work()
    {
        // 验收（审查 R136-1）：预先注入的依赖（模拟仓储构造时捕获的 IDataBaseContext）
        // 在 requiresNew 内层必须跟随当前 UoW；内层提交独立持久化，外层回滚不影响内层记录。
        // SQLite 串行化写事务（BEGIN IMMEDIATE），因此按顺序证明语义而不是并发双写。
        var (provider, _) = BuildProvider();
        Guid innerEntityId;
        Guid outerEntityId;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            // 与仓储构造函数相同的方式预先捕获依赖
            var preInjectedContext = scope.ServiceProvider.GetRequiredService<IDataBaseContext>();
            var parentNativeContext = (UowTestDbContext)preInjectedContext.GetNativeContext();

            await using (var outerScope = manager.BeginScope())
            {
                var outerUnitOfWork = outerScope.UnitOfWork;

                await using (var innerScope = manager.BeginScope(RequiresNewOptions))
                {
                    await innerScope.StartAsync();
                    var innerNativeContext = (UowTestDbContext)preInjectedContext.GetNativeContext();
                    innerNativeContext.Should().NotBeSameAs(parentNativeContext,
                        "the pre-injected dependency must follow the current unit of work during requiresNew");

                    var innerEntity = new UowTestEntity(Guid.NewGuid(), "inner");
                    innerEntityId = innerEntity.Id;
                    await preInjectedContext.Set<UowTestEntity>().AddAsync(innerEntity);
                    await innerScope.CompleteAsync();
                }

                ((UowTestDbContext)preInjectedContext.GetNativeContext()).Should().BeSameAs(parentNativeContext,
                    "the ambient context must be restored after the inner scope");

                // SQLite 单写者：外层写事务在内层完成后才开启（按顺序证明隔离语义）。
                await outerScope.StartAsync();
                var outerEntity = new UowTestEntity(Guid.NewGuid(), "outer");
                outerEntityId = outerEntity.Id;
                await preInjectedContext.Set<UowTestEntity>().AddAsync(outerEntity);

                manager.Current.Should().BeSameAs(outerUnitOfWork,
                    "the inner scope must not replace or close the outer unit of work");
                await outerScope.RollbackAsync();
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(innerEntityId)).Should().NotBeNull(
                "the inner unit of work must persist its own committed write");
            (await verifyContext.Entities.FindAsync(outerEntityId)).Should().BeNull(
                "the outer rollback must discard only the outer transaction's changes");
        }
    }

    [Fact]
    public async Task Nested_requires_new_execution_commits_inner_and_restores_parent()
    {
        var (provider, _) = BuildProvider();
        var innerEntityId = Guid.Empty;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var preInjectedContext = scope.ServiceProvider.GetRequiredService<IDataBaseContext>();
            var parentNativeContext = (UowTestDbContext)preInjectedContext.GetNativeContext();

            await using (var outerScope = manager.BeginScope())
            {
                // SQLite 单写者：外层不先开启写事务，内层 child 连接独立提交。
                var committed = await manager.ExecuteAsync(
                    async _ =>
                    {
                        ((UowTestDbContext)preInjectedContext.GetNativeContext()).Should().NotBeSameAs(
                            parentNativeContext,
                            "nested requiresNew execution must isolate the ambient context for the callback");
                        var innerEntity = new UowTestEntity(Guid.NewGuid(), "execute-async-inner");
                        innerEntityId = innerEntity.Id;
                        await preInjectedContext.Set<UowTestEntity>().AddAsync(innerEntity);
                        return true;
                    },
                    RequiresNewOptions);

                committed.Should().BeTrue();
                manager.Current.Should().BeSameAs(outerScope.UnitOfWork,
                    "the nested scope must restore the parent unit of work");
                ((UowTestDbContext)preInjectedContext.GetNativeContext()).Should().BeSameAs(parentNativeContext,
                    "the nested scope must restore the parent ambient context");
            }

            manager.CurrentOrNull.Should().BeNull();
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(innerEntityId)).Should().NotBeNull(
                "the nested unit of work must commit independently");
        }
    }

    [Fact]
    public async Task Second_level_requires_new_commits_the_context_used_by_business()
    {
        // 验收（审查 R2-136-2）：两层 requiresNew 时，最内层 UoW 必须持有自身
        // 作用域的资源；构造最内层 UoW 时不能被上一层 ambient 重定向。
        var (provider, _) = BuildProvider();
        var level2EntityId = Guid.Empty;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var preInjectedContext = scope.ServiceProvider.GetRequiredService<IDataBaseContext>();
            var parentNativeContext = (UowTestDbContext)preInjectedContext.GetNativeContext();

            await using (var outerScope = manager.BeginScope())
            {
                // SQLite 单写者：各层写事务不重叠开启，逐层构造与恢复仍被验证。
                await using (var level1Scope = manager.BeginScope(RequiresNewOptions))
                {
                    var level1NativeContext = (UowTestDbContext)preInjectedContext.GetNativeContext();
                    level1NativeContext.Should().NotBeSameAs(parentNativeContext,
                        "the first isolated level must use its own context");

                    await using (var level2Scope = manager.BeginScope(RequiresNewOptions))
                    {
                        await level2Scope.StartAsync();
                        var level2NativeContext = (UowTestDbContext)preInjectedContext.GetNativeContext();
                        level2NativeContext.Should().NotBeSameAs(level1NativeContext,
                            "the second isolated level must use its own context");

                        var entity = new UowTestEntity(Guid.NewGuid(), "level2");
                        level2EntityId = entity.Id;
                        await preInjectedContext.Set<UowTestEntity>().AddAsync(entity);
                        await level2Scope.CompleteAsync();
                    }

                    ((UowTestDbContext)preInjectedContext.GetNativeContext()).Should().BeSameAs(
                        level1NativeContext, "the first level's ambient context must be restored");
                    manager.Current.Should().BeSameAs(level1Scope.UnitOfWork);
                }

                ((UowTestDbContext)preInjectedContext.GetNativeContext()).Should().BeSameAs(
                    parentNativeContext, "the parent ambient context must be restored");
                manager.Current.Should().BeSameAs(outerScope.UnitOfWork);
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(level2EntityId)).Should().NotBeNull(
                "the innermost unit of work must commit the context that business actually used");
        }
    }

    [Fact]
    public async Task Abandoned_root_scope_ends_its_transaction_before_next_uow()
    {
        // 验收（审查 R2-136-3）：顶层 scope 未 commit/rollback 就退出时，
        // 管理器必须及时终结其未完成事务（不释放 DI 持有对象），
        // 同一请求内随后开始的新 UoW 不得遇到 “Transaction already in progress”。
        var (provider, _) = BuildProvider();
        var nextEntityId = Guid.Empty;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<UowTestDbContext>();

            await using (var abandonedScope = manager.BeginScope())
            {
                await abandonedScope.StartAsync();
                abandonedScope.State.Should().Be(UnitOfWorkState.Active);
            }

            manager.CurrentOrNull.Should().BeNull();
            dbContext.Database.CurrentTransaction.Should().BeNull(
                "an abandoned scope must terminate its pending transaction immediately");

            await using (var nextScope = manager.BeginScope())
            {
                await nextScope.StartAsync();
                var nextEntity = new UowTestEntity(Guid.NewGuid(), "after-abandon");
                nextEntityId = nextEntity.Id;
                dbContext.Entities.Add(nextEntity);
                await nextScope.CompleteAsync();
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<UowTestDbContext>();
            (await verifyContext.Entities.FindAsync(nextEntityId)).Should().NotBeNull(
                "the same request must be able to begin and commit a new unit of work afterwards");
        }
    }

    [Fact]
    public async Task Default_framework_context_follows_requires_new_and_commits_via_injected_repository()
    {
        // 验收（审查 R2-136-1）：正式默认装配（CrestCreatesDbContext 直接绑定
        // IEntityFrameworkCoreDbContext / IDataBaseContext）也必须进入 ambient 链路；
        // 预先注入的正式仓储在内层提交必须真正持久化。
        var services = new ServiceCollection();
        services.AddDbContext<CrestCreatesDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp => sp.GetRequiredService<CrestCreatesDbContext>());
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddSingleton(Mock.Of<ICurrentTenant>());
        services.AddSingleton<IDomainEventPublisher>(new RecordingDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        using var provider = services.BuildServiceProvider();
        using (var initScope = provider.CreateScope())
        {
            initScope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>().Database.EnsureCreated();
        }

        var tenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            // 与仓库构造函数相同的方式预先捕获依赖，并用正式仓储执行写入
            var preInjectedContext = scope.ServiceProvider.GetRequiredService<IDataBaseContext>();
            var parentNativeContext = (DbContext)preInjectedContext.GetNativeContext();
            var repository = new EfCoreRepository<Tenant, Guid>(preInjectedContext);

            await using (var outerScope = manager.BeginScope())
            {
                await using (var innerScope = manager.BeginScope(RequiresNewOptions))
                {
                    await innerScope.StartAsync();
                    ((DbContext)preInjectedContext.GetNativeContext()).Should().NotBeSameAs(
                        parentNativeContext,
                        "the default framework DbContext must follow the current unit of work through the platform ambient mechanism");

                    await repository.InsertAsync(new Tenant(tenantId, "Ambient Tenant"));
                    await innerScope.CompleteAsync();
                }

                ((DbContext)preInjectedContext.GetNativeContext()).Should().BeSameAs(
                    parentNativeContext, "the ambient context must be restored after the inner scope");
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            (await verifyContext.Tenants.FindAsync(tenantId)).Should().NotBeNull(
                "the repository write through the default registration must be committed by the inner unit of work");
        }

        // Rollback phase: a requiresNew write through the same default
        // registration must be discarded when the inner unit of work rolls back.
        var rollbackTenantId = Guid.NewGuid();
        using (var rollbackScope = provider.CreateScope())
        {
            var manager = rollbackScope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var injectionContext = rollbackScope.ServiceProvider.GetRequiredService<IDataBaseContext>();
            var repository = new EfCoreRepository<Tenant, Guid>(injectionContext);

            await using (var outerScope = manager.BeginScope())
            {
                await using (var innerScope = manager.BeginScope(RequiresNewOptions))
                {
                    await innerScope.StartAsync();
                    await repository.InsertAsync(new Tenant(rollbackTenantId, "Rolled Back Tenant"));
                    await innerScope.RollbackAsync();
                }
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            (await verifyContext.Tenants.FindAsync(rollbackTenantId)).Should().BeNull(
                "an inner requiresNew rollback must discard repository writes made through the default registration");
        }
    }

    [Fact]
    public async Task Domain_events_publish_after_the_commit_is_persisted()
    {
        var publisher = new RecordingDomainEventPublisher();
        var (provider, _) = BuildProvider(publisher: publisher);

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

            await using (var unitOfWorkScope = manager.BeginScope())
            {
                await unitOfWorkScope.StartAsync();
                var entity = new UowTestEntity(Guid.NewGuid(), "with-event");
                entity.AddDomainEvent(new UowTestDomainEvent { OccurredOn = DateTime.UtcNow });
                dbContext.Entities.Add(entity);
                await unitOfWorkScope.CompleteAsync();

                unitOfWorkScope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
                unitOfWorkScope.NotificationOutcome.Should().Be(UnitOfWorkNotificationOutcome.Succeeded);
            }
        }

        publisher.Published.Should().ContainSingle();
        publisher.RowVisibleDuringPublish.Should().BeTrue(
            "lifecycle/domain events must be published only after the persisted state is committed");
    }

    private (ServiceProvider Provider, RecordingDomainEventPublisher Publisher) BuildProvider(
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
        services.AddEfCoreUnitOfWork();

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
