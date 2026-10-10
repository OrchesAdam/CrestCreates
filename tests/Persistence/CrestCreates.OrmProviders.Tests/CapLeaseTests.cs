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

    public void Dispose()
    {
        foreach (var suffix in new string[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
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

        lease!.Provider.Should().Be(OrmProvider.EfCore);
        lease.ResourceIdentity.Should().BeSameAs(dbContext,
            "the physical resource identity is the effective context instance, not a logical key");
        lease.Transaction.TransactionId.Should().Be(currentTransaction!.TransactionId,
            "the lease must confirm the same transaction as the effective resource");
        lease.Transaction.NativeTransaction.Should().BeSameAs(currentTransaction,
            "the native transaction view is the same handle the resource uses (adapter-boundary borrow)");
        lease.IsValid.Should().BeTrue();
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

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ProviderCapabilityTests.CapDbContext>(options =>
            options.UseSqlite("Data Source=" + _databasePath));
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
