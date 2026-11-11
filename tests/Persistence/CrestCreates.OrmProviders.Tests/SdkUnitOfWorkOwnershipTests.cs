using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Data.SqlSugar.UnitOfWork;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.UnitOfWork;
using FluentAssertions;
using FreeSql;
using Moq;
using SqlSugar;
using Xunit;
using SqlSugarAdo = SqlSugar.IAdo;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// R3-10：SDK Provider 的 Begin/Commit 与取消/终结串行所有权；返回后无后台提交，
/// 未确认提交保留原始异常且资源仍可终结。
/// </summary>
public class SdkUnitOfWorkOwnershipTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "crest-r3-10-" + Guid.NewGuid().ToString("N") + ".db");

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
    public async Task SqlSugar_failed_abandon_is_not_reported_as_success_on_retry()
    {
        var (client, ado) = CreateSqlSugarMocks();
        ado.Setup(adapter => adapter.RollbackTran()).Throws(new ApplicationException("rollback-failed"));
        var unitOfWork = new SqlSugarUnitOfWork(client.Object, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());

        var abandon = () => unitOfWork.AbandonPendingWork();

        abandon.Should().Throw<UnitOfWorkAbandonFailureException>();
        abandon.Should().Throw<UnitOfWorkAbandonFailureException>(
            "no successful rollback has confirmed termination");
        ado.Verify(adapter => adapter.RollbackTran(), Times.Exactly(2));
    }

    [Fact]
    public async Task SqlSugar_failed_abandon_keeps_begin_rejected_and_terminates_on_retry()
    {
        var (client, ado) = CreateSqlSugarMocks();
        var attempts = 0;
        ado.Setup(adapter => adapter.RollbackTran()).Callback(() =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new ApplicationException("rollback-failed");
            }
        });
        var unitOfWork = new SqlSugarUnitOfWork(client.Object, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());

        var abandon = () => unitOfWork.AbandonPendingWork();
        abandon.Should().Throw<UnitOfWorkAbandonFailureException>();

        var begin = async () => await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        await begin.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in progress*");

        unitOfWork.Dispose();
        ado.Verify(adapter => adapter.RollbackTran(), Times.Exactly(2),
            "Dispose retries the unconfirmed rollback instead of dropping ownership");

        var second = () => unitOfWork.AbandonPendingWork();
        second.Should().NotThrow("a confirmed termination makes later abandons idempotent");
        ado.Verify(adapter => adapter.RollbackTran(), Times.Exactly(2));
    }

    [Fact]
    public async Task SqlSugar_commit_that_started_ignores_late_cancellation_and_never_rolls_back()
    {
        var (client, ado) = CreateSqlSugarMocks();
        var commitStarted = new ManualResetEventSlim();
        var releaseCommit = new ManualResetEventSlim();
        ado.Setup(adapter => adapter.CommitTran()).Callback(() =>
        {
            commitStarted.Set();
            releaseCommit.Wait();
        });
        var unitOfWork = new SqlSugarUnitOfWork(client.Object, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        using var cancellation = new CancellationTokenSource();

        var commit = unitOfWork.CommitTransactionAsync(cancellation.Token);
        commitStarted.Wait();
        cancellation.Cancel();

        commit.IsCompleted.Should().BeFalse("the await must not return while the SDK commit is still running");
        releaseCommit.Set();
        await commit;

        ado.Verify(adapter => adapter.CommitTran(), Times.Once);
        ado.Verify(adapter => adapter.RollbackTran(), Times.Never,
            "a cancellation that arrives after dispatch must not rewrite a confirmed commit");

        var second = async () => await unitOfWork.CommitTransactionAsync();
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No transaction*");
    }

    [Fact]
    public async Task SqlSugar_concurrent_commit_is_rejected_under_a_single_owner()
    {
        var (client, ado) = CreateSqlSugarMocks();
        var commitStarted = new ManualResetEventSlim();
        var releaseCommit = new ManualResetEventSlim();
        ado.Setup(adapter => adapter.CommitTran()).Callback(() =>
        {
            commitStarted.Set();
            releaseCommit.Wait();
        });
        var unitOfWork = new SqlSugarUnitOfWork(client.Object, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());

        var first = unitOfWork.CommitTransactionAsync();
        commitStarted.Wait();

        var second = async () => await unitOfWork.CommitTransactionAsync();
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in progress*");

        releaseCommit.Set();
        await first;
        ado.Verify(adapter => adapter.CommitTran(), Times.Once, "the overlap peak must stay at one native commit");
    }

    [Fact]
    public async Task SqlSugar_pre_cancelled_commit_keeps_the_transaction_terminable()
    {
        var (client, ado) = CreateSqlSugarMocks();
        var unitOfWork = new SqlSugarUnitOfWork(client.Object, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await unitOfWork.CommitTransactionAsync(cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        ado.Verify(adapter => adapter.CommitTran(), Times.Never,
            "a cancellation before dispatch must not start the commit");

        unitOfWork.AbandonPendingWork();

        ado.Verify(adapter => adapter.RollbackTran(), Times.Once,
            "the still-open transaction must remain owned and terminable after a cancelled commit");
        unitOfWork.AbandonPendingWork();
        ado.Verify(adapter => adapter.RollbackTran(), Times.Once, "abandon is idempotent after termination");
    }

    [Fact]
    public async Task SqlSugar_commit_failure_preserves_the_original_exception_and_stays_terminable()
    {
        var (client, ado) = CreateSqlSugarMocks();
        ado.Setup(adapter => adapter.CommitTran()).Throws(new ApplicationException("commit-failure"));
        var unitOfWork = new SqlSugarUnitOfWork(client.Object, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());

        var act = async () => await unitOfWork.CommitTransactionAsync();
        (await act.Should().ThrowAsync<ApplicationException>()).Which.Message.Should().Be("commit-failure");

        unitOfWork.AbandonPendingWork();
        ado.Verify(adapter => adapter.RollbackTran(), Times.Once,
            "an unconfirmed commit keeps the transaction available for termination");
    }

    [Fact]
    public async Task SqlSugar_abandon_is_rejected_while_an_operation_is_in_progress()
    {
        var (client, ado) = CreateSqlSugarMocks();
        var commitStarted = new ManualResetEventSlim();
        var releaseCommit = new ManualResetEventSlim();
        ado.Setup(adapter => adapter.CommitTran()).Callback(() =>
        {
            commitStarted.Set();
            releaseCommit.Wait();
        });
        var unitOfWork = new SqlSugarUnitOfWork(client.Object, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());

        var commit = unitOfWork.CommitTransactionAsync();
        commitStarted.Wait();

        var abandon = () => unitOfWork.AbandonPendingWork();
        abandon.Should().Throw<UnitOfWorkAbandonFailureException>()
            .WithMessage("*operation in progress*");

        releaseCommit.Set();
        await commit;
    }

    [Fact]
    public async Task FreeSql_pre_cancelled_commit_keeps_the_transaction_terminable()
    {
        var freeSql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=" + _databasePath)
            .Build();
        freeSql.CodeFirst.SyncStructure<OwnershipEntity>();
        var manager = new FreeSqlUnitOfWorkManager(freeSql);
        var unitOfWork = new FreeSqlUnitOfWork(manager, new NoOpDomainEventPublisher());

        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        await manager.Orm.Insert(new OwnershipEntity { Id = Guid.NewGuid(), Name = "pending" })
            .ExecuteAffrowsAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = async () => await unitOfWork.CommitTransactionAsync(cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        unitOfWork.AbandonPendingWork();

        (await manager.Orm.Select<OwnershipEntity>().CountAsync()).Should().Be(
            0,
            "abandon must roll back the still-owned transaction instead of leaking it in the background");
    }

    [Fact]
    public async Task FreeSql_rejects_a_second_commit_after_a_confirmed_commit()
    {
        var freeSql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=" + _databasePath)
            .Build();
        var manager = new FreeSqlUnitOfWorkManager(freeSql);
        var unitOfWork = new FreeSqlUnitOfWork(manager, new NoOpDomainEventPublisher());

        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        await unitOfWork.CommitTransactionAsync();

        var second = async () => await unitOfWork.CommitTransactionAsync();
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No transaction*");
    }

    [Fact]
    public async Task FreeSql_rollback_failure_retains_ownership_and_preserves_the_original_exception()
    {
        var native = new Mock<global::FreeSql.IUnitOfWork>();
        var attempts = 0;
        native.Setup(unitOfWork => unitOfWork.Rollback()).Callback(() =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new ApplicationException("rollback-failure");
            }
        });
        var unitOfWork = CreateFreeSqlUnitOfWork(native.Object);

        var failure = await Record.ExceptionAsync(() => unitOfWork.RollbackTransactionAsync());

        failure.Should().BeOfType<ApplicationException>().Which.Message.Should().Be("rollback-failure");

        unitOfWork.AbandonPendingWork();

        attempts.Should().Be(2, "a failed explicit rollback keeps the handle for the abandon retry");
        native.Verify(instance => instance.Dispose(), Times.Once);
    }

    [Fact]
    public async Task FreeSql_confirmed_rollback_cleanup_failure_is_reported_without_rewriting_the_rollback()
    {
        var native = new Mock<global::FreeSql.IUnitOfWork>();
        native.Setup(instance => instance.Dispose()).Throws(new ApplicationException("dispose-failure"));
        var unitOfWork = CreateFreeSqlUnitOfWork(native.Object);

        var act = async () => await unitOfWork.RollbackTransactionAsync();

        (await act.Should().ThrowAsync<UnitOfWorkRollbackCleanupFailureException>())
            .Which.InnerException!.Message.Should().Be("dispose-failure");
        native.Verify(instance => instance.Rollback(), Times.Once,
            "the confirmed rollback must not be repeated because handle cleanup failed");
    }

    private static FreeSqlUnitOfWork CreateFreeSqlUnitOfWork(global::FreeSql.IUnitOfWork native)
    {
        var freeSql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .Build();
        var unitOfWork = new FreeSqlUnitOfWork(
            new FreeSqlUnitOfWorkManager(freeSql), new NoOpDomainEventPublisher());
        typeof(FreeSqlUnitOfWork)
            .GetField("_unitOfWork", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(unitOfWork, native);
        return unitOfWork;
    }

    private static (Mock<ISqlSugarClient> Client, Mock<SqlSugarAdo> Ado) CreateSqlSugarMocks()
    {
        var ado = new Mock<SqlSugarAdo>();
        var client = new Mock<ISqlSugarClient>();
        client.SetupGet(instance => instance.Ado).Returns(ado.Object);
        return (client, ado);
    }

    public sealed class OwnershipEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
