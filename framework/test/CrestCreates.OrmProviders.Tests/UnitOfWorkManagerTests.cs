using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.OrmProviders.Abstract;
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
            using (var innerScope = manager.BeginScope(requiresNew: true))
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
    public void Begin_Should_Dispose_Ambient_Scope_With_Legacy_UnitOfWork()
    {
        var factory = new FakeUnitOfWorkFactory();
        var manager = new UnitOfWorkManager(factory);

        var unitOfWork = manager.Begin();

        manager.Current.Should().BeSameAs(factory.CreatedUnitOfWorks[0]);
        unitOfWork.Dispose();

        manager.CurrentOrNull.Should().BeNull();
        factory.CreatedUnitOfWorks[0].DisposeCount.Should().Be(1);
    }

    private sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public List<FakeUnitOfWork> CreatedUnitOfWorks { get; } = new();

        public IUnitOfWork Create(OrmProvider provider)
        {
            var unitOfWork = new FakeUnitOfWork();
            CreatedUnitOfWorks.Add(unitOfWork);
            return unitOfWork;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int DisposeCount { get; private set; }

        public Task BeginTransactionAsync()
        {
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync()
        {
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync()
        {
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync()
        {
            return Task.FromResult(0);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
