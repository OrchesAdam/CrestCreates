using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.UnitOfWork;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Context = CrestCreates.OrmProviders.Tests.AmbientChainAndOwnershipTests.ChainDbContext;
using SecondContext = CrestCreates.OrmProviders.Tests.AmbientChainAndOwnershipTests.ChainSecondaryDbContext;

namespace CrestCreates.OrmProviders.Tests;

public class Pr139Round2ReviewerTests
{
    private static ServiceProvider Build()
    {
        var connection = $"Data Source=/tmp/pr139-r2-{Guid.NewGuid():N}.db";
        var services = new ServiceCollection();
        services.AddDbContext<Context>(o => o.UseSqlite(connection));
        services.AddScoped<IDataBaseContext>(sp => new EfCoreDbContextAdapter(
            sp.GetRequiredService<Context>(), sp.GetRequiredService<UnitOfWorkChainNode>()));
        services.AddSingleton(Mock.Of<IDomainEventPublisher>());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Business_exception_must_retain_cleanup_diagnostic()
    {
        var primary = new ApplicationException("business-root");
        var manager = new UnitOfWorkManager(new Factory(new DisposeFailureUow()));
        Exception? observed = null;
        try { await manager.ExecuteAsync<int>(_ => throw primary); }
        catch (Exception exception) { observed = exception; }
        observed.Should().BeSameAs(primary);
        // With the same primary object preserved, a retained secondary failure
        // needs an attached result or diagnostic. No such result exists on the scope API.
        observed!.Data.Values.Cast<object>().Should().Contain(x => x is UnitOfWorkCleanupException);
    }

    [Fact]
    public async Task Ef_rollback_failure_must_not_report_successful_rollback()
    {
        var transaction = new Mock<IDbContextTransaction>();
        transaction.Setup(x => x.Rollback()).Throws(new InvalidOperationException("rollback-failed"));
        using var realContext = new DbContext(new DbContextOptionsBuilder<DbContext>().UseSqlite("Data Source=:memory:").Options);
        var db = new Mock<DbContext>();
        var database = new Mock<Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade>(db.Object);
        database.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        db.SetupGet(x => x.Database).Returns(database.Object);
        db.SetupGet(x => x.ChangeTracker).Returns(realContext.ChangeTracker);
        var uow = new EfCoreUnitOfWork(db.Object, Mock.Of<IDomainEventPublisher>());
        var manager = new UnitOfWorkManager(new Factory(uow));
        using var scope = manager.BeginScope();
        await scope.StartAsync();
        await scope.RollbackAsync();
        transaction.Verify(x => x.Rollback(), Times.Once);
        scope.State.Should().Be(UnitOfWorkState.Failed);
    }

    [Fact]
    public async Task Native_borrower_must_reject_unmanaged_transaction()
    {
        using var provider = Build();
        using var di = provider.CreateScope();
        var db = di.ServiceProvider.GetRequiredService<Context>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var borrower = di.ServiceProvider.GetRequiredService<IEfCoreTransactionBorrower>();
        borrower.TryBorrowCurrent(out _, out _).Should().BeFalse("no UoW scope exists");
    }

    [Fact]
    public async Task Lease_must_not_adopt_native_transaction_before_scope_start()
    {
        using var provider = Build();
        using var di = provider.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var scope = manager.BeginScope(); // No StartAsync: not a managed transaction.
        var db = di.ServiceProvider.GetRequiredService<Context>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var issuer = di.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        issuer.TryAcquireCurrentLease(out _).Should().BeFalse("StartAsync was never called");
    }

    [Fact]
    public async Task Root_scope_must_reject_second_logical_context()
    {
        using var provider = Build();
        using var di = provider.CreateScope();
        var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var first = di.ServiceProvider.GetRequiredService<Context>();
        var options = new DbContextOptionsBuilder<SecondContext>()
            .UseSqlite(first.Database.GetConnectionString()!).Options;
        using var second = new SecondContext(options);
        var secondReader = new EfCoreDbContextAdapter(second, di.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>());
        using var root = manager.BeginScope();
        await root.StartAsync();
        Action read = () => secondReader.GetNativeContext();
        read.Should().Throw<InvalidOperationException>("same chain but another context declaration is a second resource");
    }

    private sealed class Factory(IUnitOfWork uow) : IUnitOfWorkFactory
    {
        public IUnitOfWork Create(OrmProvider provider) => uow;
    }

    private sealed class DisposeFailureUow : IUnitOfWork
    {
        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public void Dispose() => throw new InvalidOperationException("secondary-dispose-failure");
    }
}
