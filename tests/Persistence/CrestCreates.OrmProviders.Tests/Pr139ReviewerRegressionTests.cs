using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Entity = CrestCreates.OrmProviders.Tests.AmbientChainAndOwnershipTests.ChainEntity;
using Context = CrestCreates.OrmProviders.Tests.AmbientChainAndOwnershipTests.ChainDbContext;
using Tenant = CrestCreates.OrmProviders.Tests.AmbientChainAndOwnershipTests.MutableCurrentTenant;

namespace CrestCreates.OrmProviders.Tests;

public class Pr139ReviewerRegressionTests
{
    private static (ServiceProvider, Tenant) Build()
    {
        var tenant = new Tenant { Id = "tenant-a" };
        var services = new ServiceCollection();
        var connectionString = $"Data Source=/tmp/pr139-{Guid.NewGuid():N}.db";
        services.AddDbContext<Context>(o => o.UseSqlite(connectionString));
        services.AddSingleton<ICurrentTenant>(tenant);
        services.AddScoped<IDataBaseContext>(sp => new EfCoreDbContextAdapter(
            sp.GetRequiredService<Context>(), sp.GetRequiredService<UnitOfWorkChainNode>(), tenant));
        services.AddScoped(sp => new EfCoreRepository<Entity, Guid>(
            sp.GetRequiredService<IDataBaseContext>(), sp.GetRequiredService<UnitOfWorkChainNode>()));
        services.AddSingleton(Mock.Of<IDomainEventPublisher>());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();
        var provider = services.BuildServiceProvider();
        using var init = provider.CreateScope();
        init.ServiceProvider.GetRequiredService<Context>().Database.EnsureCreated();
        return (provider, tenant);
    }

    [Fact]
    public async Task Abandoned_join_must_prevent_outer_commit()
    {
        var (provider, _) = Build();
        using var host = provider;
        using var di = host.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var repository = di.ServiceProvider.GetRequiredService<EfCoreRepository<Entity, Guid>>();
        await using var outer = manager.BeginScope();
        await outer.StartAsync();
        await using (var join = manager.BeginScope())
        {
            await join.StartAsync();
            await repository.InsertAsync(new Entity(Guid.NewGuid(), "abandoned-join"));
        }
        Func<Task> complete = () => outer.CompleteAsync();
        await complete.Should().ThrowAsync<UnitOfWorkRollbackOnlyException>();
    }

    [Fact]
    public async Task Explicit_rollback_must_not_leak_pending_writes_into_next_uow()
    {
        var (provider, _) = Build();
        using var host = provider;
        using var di = host.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var repository = di.ServiceProvider.GetRequiredService<EfCoreRepository<Entity, Guid>>();
        var id = Guid.NewGuid();
        await using (var first = manager.BeginScope())
        {
            await first.StartAsync();
            await repository.InsertAsync(new Entity(id, "rolled-back-pending"));
            await first.RollbackAsync();
        }
        await using (var second = manager.BeginScope())
        {
            await second.StartAsync();
            await second.CompleteAsync();
        }
        using var verify = host.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<Context>().Entities.FindAsync(id))
            .Should().BeNull("a later successful UoW must not resurrect a rolled back insert");
    }

    [Fact]
    public async Task Root_uow_must_reject_tenant_change_before_repository_access()
    {
        var (provider, tenant) = Build();
        using var host = provider;
        using var di = host.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var adapter = di.ServiceProvider.GetRequiredService<IDataBaseContext>();
        await using var root = manager.BeginScope();
        await root.StartAsync();
        tenant.Id = "tenant-b";
        Action read = () => adapter.GetNativeContext();
        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Tenant_to_host_change_must_be_rejected_inside_isolation()
    {
        var (provider, tenant) = Build();
        using var host = provider;
        using var di = host.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var adapter = di.ServiceProvider.GetRequiredService<IDataBaseContext>();
        await using var root = manager.BeginScope();
        await using var child = manager.BeginScope(new() { Propagation = UnitOfWorkPropagation.RequiresNew });
        await child.StartAsync();
        tenant.Id = null;
        Action read = () => adapter.GetNativeContext();
        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Completed_scope_must_reject_new_repository_writes()
    {
        var (provider, _) = Build();
        using var host = provider;
        using var di = host.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var repository = di.ServiceProvider.GetRequiredService<EfCoreRepository<Entity, Guid>>();
        await using var root = manager.BeginScope();
        await root.StartAsync();
        await root.CompleteAsync();
        Func<Task> write = async () => await repository.InsertAsync(new Entity(Guid.NewGuid(), "after-completion"));
        await write.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Joined_scope_must_inherit_owner_cancellation_without_deadline()
    {
        var manager = new UnitOfWorkManager(new Factory(new Fake()));
        using var cts = new CancellationTokenSource();
        await using var root = manager.BeginScope();
        await root.StartAsync(cts.Token);
        await using var join = manager.BeginScope();
        await join.StartAsync();
        cts.Cancel();
        join.ExecutionToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Joined_scope_completion_must_observe_its_deadline()
    {
        var manager = new UnitOfWorkManager(new Factory(new Fake()));
        await using var root = manager.BeginScope();
        await root.StartAsync();
        await using var join = manager.BeginScope(new() { Timeout = TimeSpan.FromMilliseconds(20) });
        await join.StartAsync();

        // Wait mechanics hardened (bounded wait for the deadline to fire); assertions unchanged.
        var waitDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (!join.ExecutionToken.IsCancellationRequested && DateTimeOffset.UtcNow < waitDeadline)
        {
            await Task.Delay(20);
        }

        join.ExecutionToken.IsCancellationRequested.Should().BeTrue();
        Func<Task> complete = () => join.CompleteAsync();
        await complete.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cleanup_failure_must_not_replace_business_exception()
    {
        var manager = new UnitOfWorkManager(new Factory(new Fake { FailDispose = true }));
        Func<Task> execute = () => manager.ExecuteAsync<int>(_ => throw new ApplicationException("business-root"));
        await execute.Should().ThrowAsync<ApplicationException>().WithMessage("business-root");
    }

    [Fact]
    public async Task Lease_must_reject_transaction_outside_managed_scope()
    {
        var (provider, _) = Build();
        using var host = provider;
        using var di = host.CreateScope();
        var db = di.ServiceProvider.GetRequiredService<Context>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var issuer = di.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        issuer.TryAcquireCurrentLease(out _).Should().BeFalse("there is no managed UoW window");
    }

    [Fact]
    public async Task Public_lease_must_not_expose_lifecycle_handle()
    {
        var (provider, _) = Build();
        using var host = provider;
        using var di = host.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var scope = manager.BeginScope();
        await scope.StartAsync();
        var issuer = di.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        issuer.TryAcquireCurrentLease(out var lease).Should().BeTrue();
        lease!.Transaction.NativeTransaction.Should().NotBeAssignableTo<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>();
    }

    [Fact]
    public async Task Confirmed_commit_must_not_become_unknown_when_handle_dispose_fails()
    {
        var confirmed = false;
        var transaction = new Mock<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>();
        transaction.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => confirmed = true).Returns(Task.CompletedTask);
        transaction.Setup(x => x.Dispose()).Throws(new InvalidOperationException("handle-cleanup-failure"));
        var db = new Mock<DbContext>();
        var database = new Mock<Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade>(db.Object);
        database.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        db.SetupGet(x => x.Database).Returns(database.Object);
        db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var providerUow = new CrestCreates.Data.EFCore.UnitOfWork.EfCoreUnitOfWork(db.Object, Mock.Of<IDomainEventPublisher>());
        var manager = new UnitOfWorkManager(new AnyFactory(providerUow));
        var scope = manager.BeginScope();
        await scope.StartAsync();
        try { await scope.CompleteAsync(); } catch (InvalidOperationException) { }
        confirmed.Should().BeTrue();
        // Disposal is intentionally deferred: the injected handle keeps throwing.
        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
    }

    private sealed class AnyFactory(IUnitOfWork uow) : IUnitOfWorkFactory
    {
        public IUnitOfWork Create(OrmProvider provider) => uow;
    }

    private sealed class Factory(Fake uow) : IUnitOfWorkFactory
    {
        public IUnitOfWork Create(OrmProvider provider) => uow;
    }

    private sealed class Fake : IUnitOfWork
    {
        public bool FailDispose { get; init; }
        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public void Dispose() { if (FailDispose) throw new InvalidOperationException("cleanup-secondary"); }
    }
}
