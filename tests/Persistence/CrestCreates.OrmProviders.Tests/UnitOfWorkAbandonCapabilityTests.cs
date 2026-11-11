using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.UnitOfWork;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// R3-30：Provider 终结/丢弃能力必须在业务前兑现；无终结确认时不得报告 RolledBack；
/// FreeSql 旧 attribute/helper 环境事务轨已退出。
/// </summary>
public class UnitOfWorkAbandonCapabilityTests
{
    [Fact]
    public async Task Declared_abandon_capability_without_abandonable_unit_of_work_fails_before_begin()
    {
        var unitOfWork = new CountingUnitOfWork();
        var registry = new UnitOfWorkProviderBindingRegistry(new[]
        {
            new UnitOfWorkProviderBinding(
                OrmProvider.FreeSql,
                _ => unitOfWork,
                supportsRequiresNew: false,
                capabilities: new UnitOfWorkProviderCapabilities
                {
                    PromptTerminationOnAbandon = true,
                    DiscardUncommittedOnAbandon = true
                })
        });
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var manager = new UnitOfWorkManager(
            new CountingUnitOfWorkFactory(unitOfWork),
            registry,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            explicitDefault: OrmProvider.FreeSql);

        using var scope = manager.BeginScope();
        var act = async () => await scope.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nameof(IUnitOfWorkAbandonable)}*");
        unitOfWork.BeginCount.Should().Be(0, "the declaration must be rejected before any transaction starts");
    }

    [Fact]
    public async Task Non_abandonable_transactional_owner_reports_unknown_instead_of_rolled_back()
    {
        var unitOfWork = new CountingUnitOfWork();
        var manager = new UnitOfWorkManager(new CountingUnitOfWorkFactory(unitOfWork));

        var scope = manager.BeginScope();
        await scope.StartAsync();
        scope.Dispose();

        scope.State.Should().Be(UnitOfWorkState.Failed);
        scope.TransactionOutcome.Should().Be(
            UnitOfWorkTransactionOutcome.Unknown,
            "the kernel cannot confirm termination of a provider without an abandon contract");
        unitOfWork.RollbackCount.Should().Be(0, "an unconfirmed rollback must not be fabricated");
    }

    [Fact]
    public async Task Non_abandonable_non_transactional_owner_keeps_not_started_outcome()
    {
        var unitOfWork = new CountingUnitOfWork();
        var manager = new UnitOfWorkManager(new CountingUnitOfWorkFactory(unitOfWork));

        var scope = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        await scope.StartAsync();
        scope.Dispose();

        scope.State.Should().Be(UnitOfWorkState.RolledBack);
        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.NotStarted);
    }

    [Fact]
    public async Task Abandonable_owner_terminates_and_reports_rolled_back()
    {
        var unitOfWork = new AbandonableCountingUnitOfWork();
        var manager = new UnitOfWorkManager(new CountingUnitOfWorkFactory(unitOfWork));

        var scope = manager.BeginScope();
        await scope.StartAsync();
        scope.Dispose();

        unitOfWork.AbandonCount.Should().Be(1);
        scope.State.Should().Be(UnitOfWorkState.RolledBack);
        scope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.RolledBack);
    }

    [Fact]
    public async Task Confirmed_rollback_cleanup_failure_keeps_rolled_back_for_non_abandonable_provider()
    {
        var unitOfWork = new RollbackCleanupFailureUnitOfWork();
        var manager = new UnitOfWorkManager(new CountingUnitOfWorkFactory(unitOfWork));

        using var scope = manager.BeginScope();
        await scope.StartAsync();
        await scope.RollbackAsync();

        scope.State.Should().Be(UnitOfWorkState.Failed, "the cleanup failure is checkable as Failed");
        scope.TransactionOutcome.Should().Be(
            UnitOfWorkTransactionOutcome.RolledBack,
            "the rollback was confirmed; only the handle cleanup failed");
        scope.RollbackTerminationFailure.Should().BeOfType<UnitOfWorkRollbackCleanupFailureException>();
    }

    [Fact]
    public void FreeSql_assembly_no_longer_exposes_the_legacy_transaction_track()
    {
        var legacy = typeof(CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager)
            .Assembly
            .GetTypes()
            .Where(type => type.FullName is
                "CrestCreates.Data.FreeSql.Attributes.TransactionalAttribute" or
                "CrestCreates.Data.FreeSql.Attributes.TransactionHelper")
            .Select(type => type.FullName)
            .ToArray();

        legacy.Should().BeEmpty(
            "the only execution chain is AddUnitOfWork + typed provider binding; " +
            "the archived ambient helper must not return");
    }

    private sealed class CountingUnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IUnitOfWork _unitOfWork;

        public CountingUnitOfWorkFactory(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public IUnitOfWork Create(OrmProvider provider) => _unitOfWork;
    }

    private class CountingUnitOfWork : IUnitOfWork
    {
        public int BeginCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task BeginTransactionAsync(
            UnitOfWorkBeginOptions options,
            CancellationToken cancellationToken = default)
        {
            BeginCount++;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public virtual Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public virtual void Dispose() => DisposeCount++;
    }

    private sealed class AbandonableCountingUnitOfWork : CountingUnitOfWork, IUnitOfWorkAbandonable
    {
        public int AbandonCount { get; private set; }

        public void AbandonPendingWork() => AbandonCount++;
    }

    private sealed class RollbackCleanupFailureUnitOfWork : CountingUnitOfWork
    {
        public override Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => throw new UnitOfWorkRollbackCleanupFailureException(
                "rollback-confirmed-cleanup-failed",
                new InvalidOperationException("dispose-failed"));
    }
}
