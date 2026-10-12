using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Data.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class UnitOfWorkManagerTests
{
    [Fact]
    public void BeginScope_Should_Reuse_Current_UnitOfWork_By_Default()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        using (var outerScope = manager.BeginScope())
        {
            using (var innerScope = manager.BeginScope())
            {
                innerScope.IsOwner.Should().BeFalse();
                innerScope.UnitOfWork.Should().BeSameAs(outerScope.UnitOfWork);
                manager.Current.Should().BeSameAs(outerScope.UnitOfWork);
            }

            manager.Current.Should().BeSameAs(outerScope.UnitOfWork);
        }

        manager.CurrentOrNull.Should().BeNull();
        factory.CreatedUnitOfWorks.Should().ContainSingle();
        factory.CreatedUnitOfWorks[0].DisposeCount.Should().Be(1);
    }

    [Fact]
    public void BeginScope_WithRequiresNew_Should_Restore_Parent_Scope_When_Disposed()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        using (var outerScope = manager.BeginScope())
        {
            using (var innerScope = manager.BeginScope(new UnitOfWorkOptions { Propagation = UnitOfWorkPropagation.RequiresNew }))
            {
                innerScope.IsOwner.Should().BeTrue();
                innerScope.UnitOfWork.Should().NotBeSameAs(outerScope.UnitOfWork);
                manager.Current.Should().BeSameAs(innerScope.UnitOfWork);
            }

            manager.Current.Should().BeSameAs(outerScope.UnitOfWork);
        }

        manager.CurrentOrNull.Should().BeNull();
        factory.CreatedUnitOfWorks.Should().HaveCount(2);
        factory.CreatedUnitOfWorks.Should().OnlyContain(unitOfWork => unitOfWork.DisposeCount == 1);
    }

    [Fact]
    public void BeginScope_Unstarted_Owner_Scope_Is_Abandoned_And_Disposed_On_Exit()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        using (var scope = manager.BeginScope())
        {
            manager.Current.Should().BeSameAs(factory.CreatedUnitOfWorks[0]);
            scope.State.Should().Be(UnitOfWorkState.Active);
        }

        manager.CurrentOrNull.Should().BeNull();
        factory.CreatedUnitOfWorks[0].DisposeCount.Should().Be(1);
        factory.CreatedUnitOfWorks[0].RollbackCount.Should().Be(0,
            "an unstarted scope never opened a transaction");
    }

    [Fact]
    public void Execute_Should_Preserve_Original_Exception_When_Rollback_Fails()
    {
        var factory = new FakeUnitOfWorkFactory { RollbackThrows = true };
        var manager = new UnitOfWorkManager(factory);

        var act = () => manager.Execute<bool>(() => throw new InvalidOperationException("business-failure"));

        act.Should().Throw<InvalidOperationException>().WithMessage("business-failure");
        manager.CurrentOrNull.Should().BeNull();
        factory.CreatedUnitOfWorks.Should().ContainSingle();
        factory.CreatedUnitOfWorks[0].DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Joined_Scope_Completion_Records_Participant_Success_Without_Committing_Outer()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        await using var outer = manager.BeginScope();
        await outer.StartAsync();

        await using (var inner = manager.BeginScope())
        {
            await inner.StartAsync();
            await inner.CompleteAsync();

            inner.IsOwner.Should().BeFalse();
            inner.State.Should().Be(UnitOfWorkState.Completed, "join completion records participant success");
            inner.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.NotStarted,
                "join completion must not claim a physical commit");

            var unitOfWork = (FakeUnitOfWork)outer.UnitOfWork;
            unitOfWork.CommitCount.Should().Be(0, "the outer unit of work is not committed by the join");
            outer.State.Should().Be(UnitOfWorkState.Active);
        }

        await outer.CompleteAsync();

        outer.State.Should().Be(UnitOfWorkState.Completed);
        outer.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
        var completed = (FakeUnitOfWork)outer.UnitOfWork;
        completed.CommitCount.Should().Be(1);
        completed.FlushCount.Should().Be(1);
    }

    [Fact]
    public async Task Joined_Scope_Failure_Marks_Owner_RollbackOnly_And_Outer_Refuses_To_Commit()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        await using var outer = manager.BeginScope();
        await outer.StartAsync();

        var act = async () =>
        {
            await using var inner = manager.BeginScope();
            await inner.StartAsync();
            try
            {
                throw new InvalidOperationException("inner-failure");
            }
            catch
            {
                await ((IUnitOfWorkScope)inner).RollbackAsync();
                throw;
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner-failure");

        var completeAct = async () => await outer.CompleteAsync();
        await completeAct.Should().ThrowAsync<UnitOfWorkRollbackOnlyException>();
        outer.State.Should().Be(UnitOfWorkState.RolledBack);

        var unitOfWork = (FakeUnitOfWork)outer.UnitOfWork;
        unitOfWork.CommitCount.Should().Be(0, "a partial failure must not be committed silently");
        unitOfWork.RollbackCount.Should().Be(1);
    }

    [Fact]
    public async Task CompleteAsync_Is_Idempotent_And_RollbackAfterCommit_Is_Rejected()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        await using var scope = manager.BeginScope();
        await scope.StartAsync();
        await scope.CompleteAsync();
        await scope.CompleteAsync(); // 幂等

        var rollbackAct = async () => await scope.RollbackAsync();
        await rollbackAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot roll back after commit*");

        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
        ((FakeUnitOfWork)scope.UnitOfWork).CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Non_Transactional_Owner_Flushes_Without_Transaction()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        await using var scope = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        await scope.StartAsync();
        await scope.CompleteAsync();

        var unitOfWork = (FakeUnitOfWork)scope.UnitOfWork;
        unitOfWork.BeginCount.Should().Be(0);
        unitOfWork.CommitCount.Should().Be(0);
        unitOfWork.FlushCount.Should().Be(1);
        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
    }

    [Fact]
    public async Task Joined_Scope_With_Conflicting_Options_Fails_Before_Execution()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        await using var outer = manager.BeginScope();
        await outer.StartAsync();

        var transactionalAct = () => manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        transactionalAct.Should().Throw<InvalidOperationException>().WithMessage("*IsTransactional*");

        var providerAct = () => manager.BeginScope(new UnitOfWorkOptions { Provider = OrmProvider.SqlSugar });
        providerAct.Should().Throw<InvalidOperationException>().WithMessage("*provider*");

        var isolationAct = () => manager.BeginScope(new UnitOfWorkOptions { IsolationLevel = System.Data.IsolationLevel.Serializable });
        isolationAct.Should().Throw<InvalidOperationException>().WithMessage("*isolation*");
    }

    [Fact]
    public async Task State_And_Results_Remain_Readable_After_Release()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        var scope = manager.BeginScope();
        await scope.StartAsync();
        await scope.CompleteAsync();
        scope.Dispose();

        scope.IsReleased.Should().BeTrue();
        scope.State.Should().Be(UnitOfWorkState.Completed);
        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);

        var act = async () => await scope.CompleteAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public List<FakeUnitOfWork> CreatedUnitOfWorks { get; } = new();

        public bool RollbackThrows { get; set; }

        public IUnitOfWork Create(OrmProvider provider)
        {
            var unitOfWork = new FakeUnitOfWork { RollbackThrows = RollbackThrows };
            CreatedUnitOfWorks.Add(unitOfWork);
            return unitOfWork;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int DisposeCount { get; private set; }

        public int BeginCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int FlushCount { get; private set; }

        public bool RollbackThrows { get; set; }

        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
        {
            BeginCount++;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            if (RollbackThrows)
            {
                throw new InvalidOperationException("rollback-failure");
            }

            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            FlushCount++;
            return Task.FromResult(0);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
