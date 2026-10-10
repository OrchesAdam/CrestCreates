using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Data.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 内核状态机、激活协议（调用方帧可见性，R2-S137-01）、截止时间与故障分层（S137-02）的专项验证。
/// </summary>
public class UnitOfWorkKernelStateTests
{
    [Fact]
    public async Task Caller_Frame_Sees_Activation_After_Async_Suspending_Begin()
    {
        var factory = new FakeUnitOfWorkFactory { SuspendBegin = true };
        var manager = new UnitOfWorkManager(factory);

        await using var scope = manager.BeginScope(new UnitOfWorkOptions { Timeout = TimeSpan.FromSeconds(5) });

        // Begin 真正异步挂起后，调用方帧必须仍能看到激活（Current 与执行 token 载体）。
        await scope.StartAsync();

        manager.Current.Should().BeSameAs(scope.UnitOfWork,
            "activation performed in the caller frame must survive the async Begin suspension");
        UnitOfWorkExecutionContext.CurrentExecutionToken.CanBeCanceled.Should().BeTrue(
            "the kernel execution token carrier must be visible to the caller after await");
        scope.ExecutionToken.CanBeCanceled.Should().BeTrue();

        await scope.CompleteAsync();
        manager.Current.Should().NotBeNull("the scope is still active until disposed");
    }

    [Fact]
    public async Task Deadline_Cancels_Execution_Token_And_Completion_Must_Not_Report_Success()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        await using var scope = manager.BeginScope(new UnitOfWorkOptions { Timeout = TimeSpan.FromMilliseconds(80) });
        await scope.StartAsync();

        await Task.Delay(200);
        scope.ExecutionToken.IsCancellationRequested.Should().BeTrue(
            "the kernel deadline must cancel the execution token even without caller cancellation");

        var act = async () => await scope.CompleteAsync();
        await act.Should().ThrowAsync<OperationCanceledException>();
        scope.State.Should().Be(UnitOfWorkState.Active, "a direct completion failure does not silently roll back");

        await scope.RollbackAsync();
        scope.State.Should().Be(UnitOfWorkState.RolledBack);
        ((FakeUnitOfWork)scope.UnitOfWork).CommitCount.Should().Be(0, "an expired deadline must not commit");
    }

    [Fact]
    public async Task Commit_Failure_Records_Unknown_And_Rollback_Attempt_Does_Not_Rewrite_It()
    {
        var factory = new FakeUnitOfWorkFactory { CommitThrows = true };
        var manager = new UnitOfWorkManager(factory);

        await using var scope = manager.BeginScope();
        await scope.StartAsync();

        var act = async () => await scope.CompleteAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit-failure");

        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Unknown,
            "a dispatched-but-unconfirmed commit must be recorded as unknown");
        scope.State.Should().Be(UnitOfWorkState.Failed);
        ((FakeUnitOfWork)scope.UnitOfWork).RollbackCount.Should().Be(1,
            "the kernel attempts to clean up locally without rewriting the unknown outcome");
    }

    [Fact]
    public async Task Post_Commit_Notification_Failure_Preserves_Committed_Fact()
    {
        var factory = new FakeUnitOfWorkFactory { NotifyThrows = true };
        var manager = new UnitOfWorkManager(factory);

        await using var scope = manager.BeginScope();
        await scope.StartAsync();

        var act = async () => await scope.CompleteAsync();
        await act.Should().ThrowAsync<UnitOfWorkPostCommitNotificationException>();

        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed,
            "the committed fact is preserved even when notifications fail");
        scope.NotificationOutcome.Should().Be(UnitOfWorkNotificationOutcome.Failed);
        scope.State.Should().Be(UnitOfWorkState.Completed);
        ((FakeUnitOfWork)scope.UnitOfWork).RollbackCount.Should().Be(0,
            "a post-commit notification failure must never be reported as a rollback");
        ((FakeUnitOfWork)scope.UnitOfWork).CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Joined_Scope_May_Tighten_Deadline_But_Not_Extend_It()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        await using var outer = manager.BeginScope(new UnitOfWorkOptions { Timeout = TimeSpan.FromSeconds(1) });
        await outer.StartAsync();

        var extendAct = () => manager.BeginScope(new UnitOfWorkOptions { Timeout = TimeSpan.FromSeconds(10) });
        extendAct.Should().Throw<InvalidOperationException>().WithMessage("*tighten*");

        await using var inner = manager.BeginScope(new UnitOfWorkOptions { Timeout = TimeSpan.FromMilliseconds(80) });
        await inner.StartAsync();
        await Task.Delay(200);

        inner.ExecutionToken.IsCancellationRequested.Should().BeTrue("the join deadline tightens the outer budget");
        outer.ExecutionToken.IsCancellationRequested.Should().BeFalse("the outer budget is unchanged");
    }

    [Fact]
    public async Task Lifecycle_order_is_flush_commit_notify_cleanup()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var manager = new UnitOfWorkManager(new SingletonUnitOfWorkFactory(unitOfWork));

        var scope = manager.BeginScope();
        await scope.StartAsync();
        await scope.CompleteAsync();

        unitOfWork.Events.Should().ContainInOrder("begin", "flush", "commit", "notify");
        unitOfWork.Events.Should().NotContain("dispose");

        scope.Dispose();
        unitOfWork.Events.Should().ContainInOrder("begin", "flush", "commit", "notify", "dispose");
    }

    [Fact]
    public async Task Non_transactional_completion_notifies_after_flush()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var manager = new UnitOfWorkManager(new SingletonUnitOfWorkFactory(unitOfWork));

        await using var scope = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        await scope.StartAsync();
        await scope.CompleteAsync();

        unitOfWork.Events.Should().ContainInOrder("flush", "notify");
        unitOfWork.Events.Should().NotContain("commit");
        scope.NotificationOutcome.Should().Be(UnitOfWorkNotificationOutcome.Succeeded);
    }

    [Fact]
    public async Task Abandonment_with_failing_rollback_records_failed_state_without_throwing()
    {
        var unitOfWork = new FailingAbandonUnitOfWork();
        var manager = new UnitOfWorkManager(new SingletonUnitOfWorkFactory(unitOfWork));

        var scope = manager.BeginScope();
        await scope.StartAsync();

        scope.Dispose();

        scope.IsReleased.Should().BeTrue();
        scope.State.Should().Be(UnitOfWorkState.Failed,
            "a failed termination is recorded as a checkable result instead of being swallowed");
    }

    private sealed class FailingAbandonUnitOfWork : IUnitOfWork, IUnitOfWorkAbandonable
    {
        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public void AbandonPendingWork()
        {
            throw new InvalidOperationException("abandon-failure");
        }

        public void Dispose()
        {
        }
    }

    private sealed class SingletonUnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IUnitOfWork _unitOfWork;

        public SingletonUnitOfWorkFactory(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public IUnitOfWork Create(OrmProvider provider) => _unitOfWork;
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork, IUnitOfWorkCommittedNotifier
    {
        public List<string> Events { get; } = new();

        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("commit");
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("rollback");
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("flush");
            return Task.FromResult(0);
        }

        public Task PublishCommittedNotificationsAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("notify");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Events.Add("dispose");
        }
    }

    private sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public List<FakeUnitOfWork> CreatedUnitOfWorks { get; } = new();

        public bool SuspendBegin { get; set; }

        public bool CommitThrows { get; set; }

        public bool NotifyThrows { get; set; }

        public IUnitOfWork Create(OrmProvider provider)
        {
            var unitOfWork = new FakeUnitOfWork
            {
                SuspendBegin = SuspendBegin,
                CommitThrows = CommitThrows,
                NotifyThrows = NotifyThrows
            };
            CreatedUnitOfWorks.Add(unitOfWork);
            return unitOfWork;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork, IUnitOfWorkCommittedNotifier
    {
        public int BeginCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int FlushCount { get; private set; }

        public bool SuspendBegin { get; set; }

        public bool CommitThrows { get; set; }

        public bool NotifyThrows { get; set; }

        public async Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
        {
            BeginCount++;
            if (SuspendBegin)
            {
                await Task.Yield();
            }
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (CommitThrows)
            {
                throw new InvalidOperationException("commit-failure");
            }

            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            FlushCount++;
            return Task.FromResult(0);
        }

        public Task PublishCommittedNotificationsAsync(CancellationToken cancellationToken = default)
        {
            if (NotifyThrows)
            {
                throw new InvalidOperationException("notification-failure");
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
