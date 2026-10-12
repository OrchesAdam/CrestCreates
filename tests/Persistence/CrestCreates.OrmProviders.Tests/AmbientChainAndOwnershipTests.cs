using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 切片 2 验收：受管链身份（descendant-or-self 可见性）、逻辑资源键准入（同链不同声明/连接/租户拒绝）、
/// 未完成退出丢弃跟踪写入（连续顶层 UoW 干净起步）、以及直接注入原生 DbContext 的边界。
/// </summary>
public class AmbientChainAndOwnershipTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"crest-uow-chain-{Guid.NewGuid():N}.db");

    private static readonly UnitOfWorkOptions RequiresNewOptions = new()
    {
        Propagation = UnitOfWorkPropagation.RequiresNew
    };

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
    public async Task Parent_reader_follows_child_frame_while_independent_scope_does_not()
    {
        var (provider, tenant) = BuildProvider();
        using var scopeR = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var managerR = scopeR.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var adapterR = scopeR.ServiceProvider.GetRequiredService<IDataBaseContext>();
        var adapterB = scopeB.ServiceProvider.GetRequiredService<IDataBaseContext>();

        var contextR = (DbContext)adapterR.GetNativeContext();
        var contextB = (DbContext)adapterB.GetNativeContext();

        await using (var outerScope = managerR.BeginScope())
        {
            await using (var childScope = managerR.BeginScope(RequiresNewOptions))
            {
                await childScope.StartAsync();

                var contextRDuringIsolation = (DbContext)adapterR.GetNativeContext();
                contextRDuringIsolation.Should().NotBeSameAs(contextR,
                    "the parent reader must follow the descendant frame within the same managed chain");
                contextRDuringIsolation.GetType().Should().Be(contextR.GetType());

                ((DbContext)adapterB.GetNativeContext()).Should().BeSameAs(contextB,
                    "an independent scope (different chain root) must never see another chain's frame");
            }

            ((DbContext)adapterR.GetNativeContext()).Should().BeSameAs(contextR,
                "the parent context must be restored after the isolated scope");
        }

        tenant.Id = "tenant-a";
    }

    [Fact]
    public async Task Same_chain_different_context_declaration_is_rejected()
    {
        var (provider, _) = BuildProvider();
        using var scopeR = provider.CreateScope();
        var managerR = scopeR.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var nodeR = scopeR.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>();

        var secondaryOptions = new DbContextOptionsBuilder<ChainSecondaryDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
        using var secondaryContext = new ChainSecondaryDbContext(secondaryOptions);
        var secondaryAdapter = new EfCoreDbContextAdapter(secondaryContext, nodeR);

        await using (var outerScope = managerR.BeginScope())
        {
            await using (var childScope = managerR.BeginScope(RequiresNewOptions))
            {
                await childScope.StartAsync();

                var act = () => secondaryAdapter.GetNativeContext();
                act.Should().Throw<InvalidOperationException>().WithMessage("*context declaration*");
            }
        }
    }

    [Fact]
    public async Task Different_connection_configuration_is_rejected()
    {
        var (provider, _) = BuildProvider();
        using var scopeR = provider.CreateScope();
        var managerR = scopeR.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var nodeR = scopeR.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>();

        var otherOptions = new DbContextOptionsBuilder<ChainDbContext>()
            .UseSqlite($"Data Source={_databasePath}-other")
            .Options;
        using var otherContext = new ChainDbContext(otherOptions);
        var otherAdapter = new EfCoreDbContextAdapter(otherContext, nodeR);

        await using (var outerScope = managerR.BeginScope())
        {
            await using (var childScope = managerR.BeginScope(RequiresNewOptions))
            {
                await childScope.StartAsync();

                var act = () => otherAdapter.GetNativeContext();
                act.Should().Throw<InvalidOperationException>().WithMessage("*connection configuration*");
            }
        }
    }

    [Fact]
    public async Task Tenant_switch_inside_active_isolation_is_rejected()
    {
        var (provider, tenant) = BuildProvider();
        tenant.Id = "tenant-a";

        using var scopeR = provider.CreateScope();
        var managerR = scopeR.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var adapterR = scopeR.ServiceProvider.GetRequiredService<IDataBaseContext>();

        await using (var outerScope = managerR.BeginScope())
        {
            await using (var childScope = managerR.BeginScope(RequiresNewOptions))
            {
                await childScope.StartAsync();

                tenant.Id = "tenant-b";
                var act = () => adapterR.GetNativeContext();
                act.Should().Throw<InvalidOperationException>().WithMessage("*tenant*");
            }
        }

        tenant.Id = "tenant-a";
    }

    [Fact]
    public async Task Consecutive_top_level_units_of_work_discard_abandoned_tracking()
    {
        var (provider, _) = BuildProvider();
        var abandonedId = Guid.Empty;
        var committedId = Guid.Empty;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var context = scope.ServiceProvider.GetRequiredService<ChainDbContext>();
            var repository = scope.ServiceProvider.GetRequiredService<EfCoreRepository<ChainEntity, Guid>>();

            await using (var firstScope = manager.BeginScope())
            {
                await firstScope.StartAsync();
                var abandoned = new ChainEntity(Guid.NewGuid(), "abandoned");
                abandonedId = abandoned.Id;
                await repository.InsertAsync(abandoned);
            }

            context.Database.CurrentTransaction.Should().BeNull(
                "an abandoned scope must terminate its pending transaction immediately");

            await using (var secondScope = manager.BeginScope())
            {
                await secondScope.StartAsync();
                var committed = new ChainEntity(Guid.NewGuid(), "committed");
                committedId = committed.Id;
                await repository.InsertAsync(committed);
                await secondScope.CompleteAsync();
            }
        }

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ChainDbContext>();
        (await verifyContext.Entities.FindAsync(committedId)).Should().NotBeNull();
        (await verifyContext.Entities.FindAsync(abandonedId)).Should().BeNull(
            "uncommitted tracked writes from the abandoned unit of work must be discarded, not carried into the next one");
    }

    [Fact]
    public async Task Directly_injected_native_context_does_not_follow_isolation()
    {
        var (provider, _) = BuildProvider();
        using var scopeR = provider.CreateScope();
        var managerR = scopeR.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var adapterR = scopeR.ServiceProvider.GetRequiredService<IDataBaseContext>();
        var directlyResolvedContext = scopeR.ServiceProvider.GetRequiredService<ChainDbContext>();

        await using (var outerScope = managerR.BeginScope())
        {
            await using (var childScope = managerR.BeginScope(RequiresNewOptions))
            {
                await childScope.StartAsync();

                var contextDuringIsolation = (DbContext)adapterR.GetNativeContext();
                contextDuringIsolation.Should().NotBeSameAs(directlyResolvedContext,
                    "IDataBaseContext-mediated access follows the isolated unit of work");

                scopeR.ServiceProvider.GetRequiredService<ChainDbContext>().Should().BeSameAs(
                    directlyResolvedContext,
                    "a directly injected native DbContext is not redirected by the platform (documented limitation)");
            }
        }
    }

    private (ServiceProvider Provider, MutableCurrentTenant Tenant) BuildProvider()
    {
        var tenant = new MutableCurrentTenant();

        var services = new ServiceCollection();
        services.AddDbContext<ChainDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddSingleton<ICurrentTenant>(tenant);
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp => new EfCoreDbContextAdapter(
            sp.GetRequiredService<ChainDbContext>(),
            sp.GetRequiredService<UnitOfWorkChainNode>(),
            sp.GetService<ICurrentTenant>()));
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddScoped(sp => new EfCoreRepository<ChainEntity, Guid>(
            sp.GetRequiredService<IDataBaseContext>(),
            sp.GetRequiredService<UnitOfWorkChainNode>()));
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ChainDbContext>().Database.EnsureCreated();
        }

        return (provider, tenant);
    }

    public sealed class ChainDbContext : DbContext
    {
        public ChainDbContext(DbContextOptions<ChainDbContext> options)
            : base(options)
        {
        }

        public DbSet<ChainEntity> Entities => Set<ChainEntity>();
    }

    public sealed class ChainSecondaryDbContext : DbContext
    {
        public ChainSecondaryDbContext(DbContextOptions<ChainSecondaryDbContext> options)
            : base(options)
        {
        }

        public DbSet<ChainEntity> Entities => Set<ChainEntity>();
    }

    public sealed class ChainEntity : AggregateRoot<Guid>
    {
        public ChainEntity()
        {
        }

        public ChainEntity(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class MutableCurrentTenant : ICurrentTenant
    {
        public ITenantInfo? Tenant => null;

        public string? Id { get; set; }

        public Task<IDisposable> ChangeAsync(string tenantId)
        {
            throw new NotSupportedException();
        }

        public IDisposable Change(ITenantInfo tenant)
        {
            throw new NotSupportedException();
        }

        public void SetTenantId(string tenantId)
        {
            Id = tenantId;
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
