using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 切片 5 验收：CAP 最小参与 lease —— 同一 Connection/Transaction 关联可确认、错资源/终态拒绝、
/// live view 失效语义、公开参与契约不含生命周期操作。
/// </summary>
public class CapLeaseTests : IDisposable
{
    private readonly string _databasePath =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "crest-uow-lease-" + Guid.NewGuid().ToString("N") + ".db");

    private readonly System.Collections.Generic.List<string> _extraDatabasePaths = new();

    public void Dispose()
    {
        foreach (var databasePath in new[] { _databasePath }.Concat(_extraDatabasePaths))
        {
            foreach (var suffix in new string[] { string.Empty, "-wal", "-shm" })
            {
                var path = databasePath + suffix;
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
        }
    }

    private string RegisterExtraPath(string path)
    {
        _extraDatabasePaths.Add(path);
        return path;
    }

    [Fact]
    public async Task Lease_proves_same_connection_and_transaction_association()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        var dbContext = (ProviderCapabilityTests.CapDbContext)scope.ServiceProvider
            .GetRequiredService<IDataBaseContext>().GetNativeContext();

        await using var unitOfWorkScope = manager.BeginScope();
        await unitOfWorkScope.StartAsync();

        leaseProvider.TryAcquireCurrentLease(out var lease).Should().BeTrue();
        lease.Should().NotBeNull();

        var currentTransaction = dbContext.Database.CurrentTransaction;
        currentTransaction.Should().NotBeNull();

        lease!.Resource.Provider.Should().Be(OrmProvider.EfCore);
        lease.Resource.ResourceKind.Should().Be(typeof(ProviderCapabilityTests.CapDbContext).FullName,
            "the resource identity exposes the declaration kind, not the mutable instance");
        lease.Resource.InstanceId.Should().NotBeEmpty();
        lease.Transaction.TransactionId.Should().Be(currentTransaction!.TransactionId,
            "the lease must confirm the same transaction as the effective resource");
        lease.Transaction.NativeTransaction.Should().BeOfType<UnitOfWorkNativeTransactionMarker>(
            "the public view carries a non-operable marker, never the lifecycle handle");
        ((UnitOfWorkNativeTransactionMarker)lease.Transaction.NativeTransaction).TransactionId
            .Should().Be(lease.Transaction.TransactionId);
        lease.IsValid.Should().BeTrue();

        // 原生句柄借用走 Provider 适配边界（不得经公开 lease 取得）。
        var borrower = scope.ServiceProvider.GetRequiredService<CrestCreates.Data.EFCore.UnitOfWork.IEfCoreTransactionBorrower>();
        borrower.TryBorrowCurrent(out var borrowedContext, out var borrowedTransaction).Should().BeTrue();
        borrowedContext.Should().BeSameAs(dbContext);
        borrowedTransaction.Should().BeSameAs(currentTransaction);
    }

    [Fact]
    public async Task Lease_is_rejected_without_transaction_and_invalidated_after_completion()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();

        // 无 scope：拒绝
        leaseProvider.TryAcquireCurrentLease(out _).Should().BeFalse();

        // 非事务模式：拒绝
        await using (var nonTransactional = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false }))
        {
            await nonTransactional.StartAsync();
            leaseProvider.TryAcquireCurrentLease(out _).Should().BeFalse(
                "a non-transactional unit of work has no transaction resource to participate in");
        }

        // 活动事务内获取；完结后 live view 失效且不可再获取
        UnitOfWorkTransactionLease? heldLease;
        var leaseScope = manager.BeginScope();
        await leaseScope.StartAsync();
        leaseProvider.TryAcquireCurrentLease(out heldLease).Should().BeTrue();
        heldLease!.IsValid.Should().BeTrue();

        await leaseScope.CompleteAsync();
        heldLease.IsValid.Should().BeFalse("the live view must invalidate once the transaction is completed");
        leaseProvider.TryAcquireCurrentLease(out _).Should().BeFalse();

        leaseScope.Dispose();
    }

    [Fact]
    public void Participation_contract_exposes_no_lifecycle_operations()
    {
        var lifecycleNames = new[] { "Commit", "CommitAsync", "Rollback", "RollbackAsync", "Dispose", "DisposeAsync" };

        typeof(UnitOfWorkTransactionLease).GetMethods()
            .Select(method => method.Name)
            .Should().NotIntersectWith(lifecycleNames,
                "the participation contract must not expose transaction lifecycle operations");
        typeof(IUnitOfWorkTransactionIdentity).GetMethods()
            .Select(method => method.Name)
            .Should().NotIntersectWith(lifecycleNames,
                "the identity view must stay read-only");
    }

    [Fact]
    public async Task Borrower_and_lease_reject_not_started_carrier_with_native_transaction()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        var borrower = scope.ServiceProvider.GetRequiredService<CrestCreates.Data.EFCore.UnitOfWork.IEfCoreTransactionBorrower>();
        var dbContext = (ProviderCapabilityTests.CapDbContext)scope.ServiceProvider
            .GetRequiredService<IDataBaseContext>().GetNativeContext();

        await using var notStarted = manager.BeginScope(); // 有载体 ≠ 受管事务已开始
        await using var native = await dbContext.Database.BeginTransactionAsync();

        leaseProvider.TryAcquireCurrentLease(out _).Should().BeFalse(
            "an Active carrier that never ran StartAsync must not adopt an external transaction");
        borrower.TryBorrowCurrent(out _, out _).Should().BeFalse(
            "native borrowing requires a started managed window, not just a carrier");
    }

    [Fact]
    public async Task Borrower_and_lease_reject_non_transactional_window_with_native_transaction()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        var borrower = scope.ServiceProvider.GetRequiredService<CrestCreates.Data.EFCore.UnitOfWork.IEfCoreTransactionBorrower>();
        var dbContext = (ProviderCapabilityTests.CapDbContext)scope.ServiceProvider
            .GetRequiredService<IDataBaseContext>().GetNativeContext();

        await using var nonTransactional = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        await nonTransactional.StartAsync();
        await using var native = await dbContext.Database.BeginTransactionAsync();

        leaseProvider.TryAcquireCurrentLease(out _).Should().BeFalse(
            "a non-transactional window has no managed transaction even when the resource holds one");
        borrower.TryBorrowCurrent(out _, out _).Should().BeFalse(
            "an external transaction opened inside a non-transactional window is not managed");
    }

    [Fact]
    public async Task Borrower_and_lease_reject_independent_root_with_native_transaction()
    {
        var providerA = BuildProvider();
        var providerB = BuildProvider(RegisterExtraPath(_databasePath + "-independent"));
        using var scopeA = providerA.CreateScope();
        using var scopeB = providerB.CreateScope();

        var managerA = scopeA.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProviderB = scopeB.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        var borrowerB = scopeB.ServiceProvider.GetRequiredService<CrestCreates.Data.EFCore.UnitOfWork.IEfCoreTransactionBorrower>();
        var dbContextB = (ProviderCapabilityTests.CapDbContext)scopeB.ServiceProvider
            .GetRequiredService<IDataBaseContext>().GetNativeContext();

        await using var managedA = managerA.BeginScope();
        await managedA.StartAsync();
        await using var nativeB = await dbContextB.Database.BeginTransactionAsync();

        leaseProviderB.TryAcquireCurrentLease(out _).Should().BeFalse(
            "an independent root (different chain, different resource) must not see another chain's window");
        borrowerB.TryBorrowCurrent(out _, out _).Should().BeFalse(
            "borrowing must follow the same chain visibility as lease issuance");
    }

    [Fact]
    public async Task Borrower_and_lease_reject_replaced_transaction()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();
        var borrower = scope.ServiceProvider.GetRequiredService<CrestCreates.Data.EFCore.UnitOfWork.IEfCoreTransactionBorrower>();
        var dbContext = (ProviderCapabilityTests.CapDbContext)scope.ServiceProvider
            .GetRequiredService<IDataBaseContext>().GetNativeContext();

        await using var managed = manager.BeginScope();
        await managed.StartAsync();

        // 手动终结 UoW 的事务并在同一资源上替换为另一个事务：
        // 受管关联仍指向原事务，被替换后的事务不受平台认领。
        var original = dbContext.Database.CurrentTransaction!;
        await original.RollbackAsync();
        await original.DisposeAsync();
        await using var replaced = await dbContext.Database.BeginTransactionAsync();

        leaseProvider.TryAcquireCurrentLease(out _).Should().BeFalse(
            "the window's recorded transaction was replaced; a different transaction is not managed");
        borrower.TryBorrowCurrent(out _, out _).Should().BeFalse(
            "borrowing must match the owner window's recorded resource/transaction association");

        // 收尾：原受管事务已被外部终结，回滚路径记录终结失败但不抛次级异常。
        await managed.RollbackAsync();
        managed.State.Should().Be(UnitOfWorkState.Failed);
    }

    [Fact]
    public async Task Issued_lease_must_invalidate_on_rollback_and_release()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();

        // 回滚（确认）后失效
        var rollbackScope = manager.BeginScope();
        await rollbackScope.StartAsync();
        leaseProvider.TryAcquireCurrentLease(out var rolledBackLease).Should().BeTrue();
        await rollbackScope.RollbackAsync();
        rolledBackLease!.IsValid.Should().BeFalse(
            "a rollback completes the owner window even before the scope is released");
        rollbackScope.Dispose();

        // 直接释放（未完成退出 → 放弃终结）后失效
        var releasedScope = manager.BeginScope();
        await releasedScope.StartAsync();
        leaseProvider.TryAcquireCurrentLease(out var releasedLease).Should().BeTrue();
        releasedScope.Dispose();
        releasedLease!.IsValid.Should().BeFalse(
            "release invalidates the live view without relying on the provider's handle cleanup");
    }

    [Fact]
    public async Task Old_lease_must_not_revive_with_subsequent_scope()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<IUnitOfWorkTransactionLeaseProvider>();

        var first = manager.BeginScope();
        await first.StartAsync();
        leaseProvider.TryAcquireCurrentLease(out var firstLease).Should().BeTrue();
        await first.CompleteAsync();
        first.Dispose();
        firstLease!.IsValid.Should().BeFalse("the first window completed");

        // 后续 scope 在同一资源上开启新事务：旧 lease 绑定原 owner，不得因此重新有效。
        await using var second = manager.BeginScope();
        await second.StartAsync();
        leaseProvider.TryAcquireCurrentLease(out var secondLease).Should().BeTrue();
        firstLease.IsValid.Should().BeFalse(
            "an old lease is bound to the window it was issued for, not to the current nearest owner");
        secondLease!.IsValid.Should().BeTrue();
    }

    private ServiceProvider BuildProvider(string? databasePath = null)
    {
        var path = databasePath ?? _databasePath;
        var services = new ServiceCollection();
        services.AddDbContext<ProviderCapabilityTests.CapDbContext>(options =>
            options.UseSqlite("Data Source=" + path));
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<ProviderCapabilityTests.CapDbContext>()));
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ProviderCapabilityTests.CapDbContext>().Database.EnsureCreated();
        }

        return provider;
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
