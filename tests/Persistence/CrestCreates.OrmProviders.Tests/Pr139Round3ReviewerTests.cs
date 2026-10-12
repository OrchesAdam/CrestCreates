using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Data.EFCore.UnitOfWork;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class Pr139Round3ReviewerTests
{
    [Fact]
    public async Task Issued_lease_must_expire_when_confirmed_commit_cleanup_fails()
    {
        bool failDispose = true;
        var transaction = new Mock<IDbContextTransaction>();
        transaction.SetupGet(x => x.TransactionId).Returns(Guid.NewGuid());
        transaction.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(x => x.Dispose()).Callback(() => { if (failDispose) throw new InvalidOperationException("dispose-failed"); });
        using var realContext = new DbContext(new DbContextOptionsBuilder<DbContext>().UseSqlite("Data Source=:memory:").Options);
        var db = new Mock<DbContext>();
        var database = new Mock<DatabaseFacade>(db.Object);
        database.SetupGet(x => x.CurrentTransaction).Returns(transaction.Object);
        database.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        db.SetupGet(x => x.Database).Returns(database.Object);
        db.SetupGet(x => x.ChangeTracker).Returns(realContext.ChangeTracker);
        db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var services = new ServiceCollection();
        services.AddScoped<IDataBaseContext>(sp => new EfCoreDbContextAdapter(db.Object, sp.GetRequiredService<UnitOfWorkChainNode>()));
        services.AddSingleton(Mock.Of<IDomainEventPublisher>());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();
        using var provider = services.BuildServiceProvider();
        using var di = provider.CreateScope();
        try
        {
            var manager = di.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var scope = manager.BeginScope();
            await scope.StartAsync();
            var issuer = di.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
            issuer.TryAcquireCurrentLease(out var lease).Should().BeTrue();
            lease!.IsValid.Should().BeTrue();
            await Assert.ThrowsAsync<UnitOfWorkCommitCleanupFailureException>(() => scope.CompleteAsync());
            scope.State.Should().Be(UnitOfWorkState.Failed);
            scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
            issuer.TryAcquireCurrentLease(out _).Should().BeFalse();
            lease.IsValid.Should().BeFalse("the owner is terminal even if failed handle cleanup leaves CurrentTransaction unchanged");
        }
        finally { failDispose = false; }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmed_rollback_must_survive_cleanup_failure_on_all_exit_paths(bool flushFailure)
    {
        bool failDispose = true;
        var transaction = new Mock<IDbContextTransaction>();
        transaction.Setup(x => x.Dispose()).Callback(() => { if (failDispose) throw new InvalidOperationException("dispose-failed"); });
        using var realContext = new DbContext(new DbContextOptionsBuilder<DbContext>().UseSqlite("Data Source=:memory:").Options);
        var db = new Mock<DbContext>();
        var database = new Mock<DatabaseFacade>(db.Object);
        database.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        db.SetupGet(x => x.Database).Returns(database.Object);
        db.SetupGet(x => x.ChangeTracker).Returns(realContext.ChangeTracker);
        db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ApplicationException("flush-failed"));
        var manager = new UnitOfWorkManager(new Factory(new EfCoreUnitOfWork(db.Object, Mock.Of<IDomainEventPublisher>())));
        var scope = manager.BeginScope();
        try
        {
            await scope.StartAsync();
            if (flushFailure)
                await Assert.ThrowsAsync<ApplicationException>(() => scope.CompleteAsync());
            else
                Assert.Throws<UnitOfWorkCleanupException>(() => scope.Dispose());
            transaction.Verify(x => x.Rollback(), Times.Once);
            scope.State.Should().Be(UnitOfWorkState.Failed);
            scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.RolledBack,
                "rollback succeeded and only subsequent handle cleanup failed");
        }
        finally { failDispose = false; scope.Dispose(); }
    }

    private sealed class Factory(IUnitOfWork uow) : IUnitOfWorkFactory
    {
        public IUnitOfWork Create(OrmProvider provider) => uow;
    }

}
