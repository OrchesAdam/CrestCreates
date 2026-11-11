using System;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// R3-09：同一原生事务的包装身份/终态一致；提交结果未知时不得由包装器宣称已回滚。
/// </summary>
public class EfCoreTransactionWrapperTests
{
    [Fact]
    public async Task Rollback_failure_is_not_replaced_by_cleanup_failure()
    {
        using var context = CreateContextWithFakeTransactionManager(out var transactionManager);
        var unitOfWork = new EfCoreUnitOfWork(context, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        transactionManager!.RollbackFailure = "rollback-failure";
        transactionManager.DisposeFailure = "dispose-failure";
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(context.Database.CurrentTransaction!, context);

        var failure = await Record.ExceptionAsync(() => unitOfWork.RollbackTransactionAsync());

        failure.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be(
            "rollback-failure",
            "the cleanup failure is secondary and must not replace the original rollback failure");
        wrapper.IsOutcomeUnknown.Should().BeTrue();
        transactionManager.RollbackCalls.Should().Be(1);

        transactionManager.RollbackFailure = null;
        transactionManager.DisposeFailure = null;
        unitOfWork.AbandonPendingWork();

        transactionManager.RollbackCalls.Should().Be(2, "an unconfirmed rollback keeps the handle for retry");
        wrapper.IsOutcomeUnknown.Should().BeTrue("a confirmed retry must not rewrite the earlier unknown outcome");
    }

    [Fact]
    public async Task Confirmed_rollback_cleanup_failure_keeps_the_rolled_back_fact()
    {
        using var context = CreateContextWithFakeTransactionManager(out var transactionManager);
        var unitOfWork = new EfCoreUnitOfWork(context, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        transactionManager!.DisposeFailure = "dispose-failure";
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(context.Database.CurrentTransaction!, context);

        var act = async () => await unitOfWork.RollbackTransactionAsync();

        (await act.Should().ThrowAsync<UnitOfWorkRollbackCleanupFailureException>())
            .Which.InnerException!.Message.Should().Be("dispose-failure");
        wrapper.IsRolledBack.Should().BeTrue("the rollback was confirmed before handle cleanup failed");
        wrapper.IsCompleted.Should().BeTrue();
        wrapper.IsOutcomeUnknown.Should().BeFalse();
    }

    [Fact]
    public async Task Abandon_updates_shared_wrapper_facts()
    {
        using var context = CreateContext(openConnection: true);
        var unitOfWork = new EfCoreUnitOfWork(context, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(context.Database.CurrentTransaction!, context);

        unitOfWork.AbandonPendingWork();

        wrapper.IsRolledBack.Should().BeTrue("the formal abandon confirmed the native rollback");
        wrapper.IsCompleted.Should().BeTrue();
        wrapper.IsOutcomeUnknown.Should().BeFalse();
    }

    [Fact]
    public async Task Abandon_rollback_failure_marks_unknown_and_keeps_ownership_for_retry()
    {
        using var context = CreateContextWithFakeTransactionManager(out var transactionManager);
        var unitOfWork = new EfCoreUnitOfWork(context, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        transactionManager!.RollbackFailure = "rollback-failure";
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(context.Database.CurrentTransaction!, context);

        var first = () => unitOfWork.AbandonPendingWork();
        first.Should().Throw<UnitOfWorkAbandonFailureException>()
            .Which.InnerException!.Message.Should().Be("rollback-failure");

        wrapper.IsOutcomeUnknown.Should().BeTrue();
        wrapper.IsRolledBack.Should().BeFalse();
        wrapper.IsCompleted.Should().BeTrue();
        transactionManager.RollbackCalls.Should().Be(1);

        transactionManager.RollbackFailure = null;
        unitOfWork.AbandonPendingWork();
        transactionManager.RollbackCalls.Should().Be(2, "an unconfirmed abandon keeps the transaction for retry");
        wrapper.IsOutcomeUnknown.Should().BeTrue("a confirmed retry must not rewrite the earlier unknown outcome");
    }

    [Fact]
    public async Task Abandon_cleanup_failure_keeps_the_confirmed_rollback_fact()
    {
        using var context = CreateContextWithFakeTransactionManager(out var transactionManager);
        var unitOfWork = new EfCoreUnitOfWork(context, new NoOpDomainEventPublisher());
        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        transactionManager!.DisposeFailure = "dispose-failure";
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(context.Database.CurrentTransaction!, context);

        var act = () => unitOfWork.AbandonPendingWork();
        act.Should().Throw<UnitOfWorkAbandonCleanupFailureException>();

        wrapper.IsRolledBack.Should().BeTrue("the rollback was confirmed before handle cleanup failed");
        wrapper.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Kernel_abandon_marks_the_shared_wrapper_rolled_back()
    {
        var services = new ServiceCollection();
        services.AddDbContext<WrapperDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<WrapperDbContext>()));
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        var unitOfWorkScope = manager.BeginScope();
        await unitOfWorkScope.StartAsync();
        var dataBaseContext = scope.ServiceProvider.GetRequiredService<IDataBaseContext>();
        var wrapper = (EfCoreDataBaseTransaction)dataBaseContext.CurrentTransaction!;

        unitOfWorkScope.Dispose();

        wrapper.IsRolledBack.Should().BeTrue("the kernel abandon path must publish the same rollback fact");
        wrapper.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Repeated_lookup_returns_the_same_wrapper_and_transaction_identity()
    {
        using var context = CreateContext();
        var native = new StubDbContextTransaction();

        var first = EfCoreDataBaseTransaction.GetOrCreate(native, context);
        var second = EfCoreDataBaseTransaction.GetOrCreate(native, context);

        second.Should().BeSameAs(first);
        second.TransactionId.Should().Be(first.TransactionId);
        first.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Commit_failure_is_unknown_and_does_not_claim_a_rollback()
    {
        using var context = CreateContext();
        var native = new StubDbContextTransaction { CommitFailure = "commit-failure" };
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(native, context);

        var act = async () => await wrapper.CommitAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be("commit-failure");
        wrapper.IsCommitted.Should().BeFalse();
        wrapper.IsRolledBack.Should().BeFalse("an unconfirmed commit must not be reported as rolled back");
        wrapper.IsOutcomeUnknown.Should().BeTrue();
        wrapper.IsCompleted.Should().BeTrue();
        native.RollbackCount.Should().Be(0, "the wrapper must not turn an unknown commit into a rollback");

        var retry = async () => await wrapper.CommitAsync();
        await retry.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been completed*");
        native.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Rollback_failure_is_unknown_and_keeps_the_original_exception()
    {
        using var context = CreateContext();
        var native = new StubDbContextTransaction { RollbackFailure = "rollback-failure" };
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(native, context);

        var act = async () => await wrapper.RollbackAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be("rollback-failure");
        wrapper.IsRolledBack.Should().BeFalse();
        wrapper.IsCommitted.Should().BeFalse();
        wrapper.IsOutcomeUnknown.Should().BeTrue();
    }

    [Fact]
    public async Task Confirmed_commit_is_visible_from_every_wrapper_of_the_same_transaction()
    {
        using var context = CreateContext();
        var native = new StubDbContextTransaction();
        var first = EfCoreDataBaseTransaction.GetOrCreate(native, context);

        await first.CommitAsync();

        var second = EfCoreDataBaseTransaction.GetOrCreate(native, context);
        second.IsCommitted.Should().BeTrue();
        second.IsCompleted.Should().BeTrue();
        native.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_commit_is_rejected_while_the_first_operation_is_in_progress()
    {
        using var context = CreateContext();
        var native = new StubDbContextTransaction { BlockCommit = true };
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(native, context);

        var firstCommit = wrapper.CommitAsync();
        await native.CommitStarted.Task;

        var second = async () => await wrapper.CommitAsync();
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*operation in progress*");

        native.ReleaseCommit();
        await firstCommit;
        wrapper.IsCommitted.Should().BeTrue();
        native.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Formal_unit_of_work_marks_the_wrapper_committed()
    {
        using var context = CreateContext(openConnection: true);
        var unitOfWork = new EfCoreUnitOfWork(context, new NoOpDomainEventPublisher());

        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        var native = context.Database.CurrentTransaction!;
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(native, context);

        await unitOfWork.CommitTransactionAsync();

        wrapper.IsCommitted.Should().BeTrue("the confirmed database commit is the same fact for every wrapper");
        wrapper.IsRolledBack.Should().BeFalse();
        wrapper.IsOutcomeUnknown.Should().BeFalse();
    }

    [Fact]
    public async Task Formal_unit_of_work_marks_the_wrapper_rolled_back()
    {
        using var context = CreateContext(openConnection: true);
        var unitOfWork = new EfCoreUnitOfWork(context, new NoOpDomainEventPublisher());

        await unitOfWork.BeginTransactionAsync(new UnitOfWorkBeginOptions());
        var native = context.Database.CurrentTransaction!;
        var wrapper = EfCoreDataBaseTransaction.GetOrCreate(native, context);

        await unitOfWork.RollbackTransactionAsync();

        wrapper.IsRolledBack.Should().BeTrue();
        wrapper.IsCommitted.Should().BeFalse();
    }

    private static DbContext CreateContext(bool openConnection = false)
    {
        var context = new DbContext(
            new DbContextOptionsBuilder<DbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options);
        if (openConnection)
        {
            context.Database.OpenConnection();
        }

        return context;
    }

    private static DbContext CreateContextWithFakeTransactionManager(out FakeTransactionManager? manager)
    {
        var context = new DbContext(
            new DbContextOptionsBuilder<DbContext>()
                .UseSqlite("Data Source=:memory:")
                .ReplaceService<IDbContextTransactionManager, FakeTransactionManager>()
                .Options);
        manager = context.GetService<IDbContextTransactionManager>() as FakeTransactionManager;
        return context;
    }

    public sealed class WrapperDbContext : DbContext
    {
        public WrapperDbContext(DbContextOptions<WrapperDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class FakeTransactionManager : IDbContextTransactionManager
    {
        private string? _rollbackFailure;
        private string? _disposeFailure;

        public StubDbContextTransaction? Current { get; private set; }

        IDbContextTransaction? IDbContextTransactionManager.CurrentTransaction => Current;

        public string? RollbackFailure
        {
            get => _rollbackFailure;
            set
            {
                _rollbackFailure = value;
                if (Current is not null)
                {
                    Current.RollbackFailure = value;
                }
            }
        }

        public string? DisposeFailure
        {
            get => _disposeFailure;
            set
            {
                _disposeFailure = value;
                if (Current is not null)
                {
                    Current.DisposeFailure = value;
                }
            }
        }

        public int RollbackCalls => Current?.RollbackCount ?? 0;

        public IDbContextTransaction BeginTransaction()
        {
            Current = new StubDbContextTransaction
            {
                RollbackFailure = _rollbackFailure,
                DisposeFailure = _disposeFailure
            };
            return Current;
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(BeginTransaction());

        public void CommitTransaction() => Current?.Commit();

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Current?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

        public void RollbackTransaction() => Current?.Rollback();

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Current?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;

        public void ResetState()
        {
        }

        public Task ResetStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubDbContextTransaction : IDbContextTransaction
    {
        private readonly TaskCompletionSource _commitStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _commitRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid TransactionId { get; } = Guid.NewGuid();

        public bool SupportsSavepoints => false;

        public string? CommitFailure { get; init; }

        public string? RollbackFailure { get; set; }

        public string? DisposeFailure { get; set; }

        public bool BlockCommit { get; init; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int DisposeCount { get; private set; }

        public TaskCompletionSource CommitStarted => _commitStarted;

        public void ReleaseCommit() => _commitRelease.TrySetResult();

        public void Commit() => CommitAsync().GetAwaiter().GetResult();

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            if (BlockCommit)
            {
                _commitStarted.TrySetResult();
                await _commitRelease.Task;
            }

            if (CommitFailure is not null)
            {
                throw new InvalidOperationException(CommitFailure);
            }
        }

        public void Rollback() => RollbackAsync().GetAwaiter().GetResult();

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return RollbackFailure is null
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException(RollbackFailure));
        }

        public void CreateSavepoint(string name) => throw new NotSupportedException();

        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void RollbackToSavepoint(string name) => throw new NotSupportedException();

        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void ReleaseSavepoint(string name) => throw new NotSupportedException();

        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void ClearSavepoints()
        {
        }

        public void Dispose()
        {
            DisposeCount++;
            if (DisposeFailure is not null)
            {
                throw new InvalidOperationException(DisposeFailure);
            }
        }

        public ValueTask DisposeAsync() => default;
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
