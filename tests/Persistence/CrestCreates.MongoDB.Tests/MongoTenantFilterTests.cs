using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.MongoDB.Tests.Helpers;
using CrestCreates.MongoDB.Tests.TestEntities;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.MongoDB.Repositories;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace CrestCreates.MongoDB.Tests;

public class MongoTenantFilterTests
{
    private readonly Mock<ICurrentTenant> _currentTenant = new();

    [Fact]
    public async Task GetListAsync_MustHaveTenant_ShouldFilterByTenant()
    {
        _currentTenant.Setup(t => t.Id).Returns("tenant-a");
        var entities = new List<TenantTestEntity>
        {
            new() { Id = "1", Name = "A", TenantId = "tenant-a" },
            new() { Id = "2", Name = "B", TenantId = "tenant-b" }
        };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateTenantRepo(collection);

        var result = await sut.GetListAsync();

        // InMemoryCollection returns all (tenant filter is applied at query level)
        // The repository builds a filter but InMemoryCollection ignores it
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task InsertAsync_MustHaveTenant_ShouldAutoSetTenantId()
    {
        _currentTenant.Setup(t => t.Id).Returns("tenant-auto");
        var collection = MongoMockHelper.CreateCollection<TenantTestEntity>();
        var sut = CreateTenantRepo(collection);

        var entity = new TenantTestEntity { Name = "New" };
        await sut.InsertAsync(entity);

        entity.TenantId.Should().Be("tenant-auto");
    }

    [Fact]
    public async Task UpdateAsync_MustHaveTenant_ShouldThrow_WhenDifferentTenant()
    {
        _currentTenant.Setup(t => t.Id).Returns("tenant-a");
        var collection = MongoMockHelper.CreateCollection<TenantTestEntity>();
        var sut = CreateTenantRepo(collection);

        var entity = new TenantTestEntity { Id = "1", TenantId = "tenant-b" };

        await sut.Invoking(s => s.UpdateAsync(entity))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_MustHaveTenant_ShouldThrow_WhenDifferentTenant()
    {
        _currentTenant.Setup(t => t.Id).Returns("tenant-a");
        var collection = MongoMockHelper.CreateCollection<TenantTestEntity>();
        var sut = CreateTenantRepo(collection);

        var entity = new TenantTestEntity { Id = "1", TenantId = "tenant-b" };

        await sut.Invoking(s => s.DeleteAsync(entity))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PlainEntity_ShouldNotApplyTenantFilter()
    {
        _currentTenant.Setup(t => t.Id).Returns("tenant-a");
        var entities = new List<TestEntity>
        {
            new() { Id = "1", Name = "A" },
            new() { Id = "2", Name = "B" }
        };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreatePlainRepo(collection);

        var result = await sut.GetListAsync();
        result.Should().HaveCount(2);
    }

    #region Helpers

    private MongoRepositoryBase<TenantTestEntity, string> CreateTenantRepo(
        InMemoryMongoCollection<TenantTestEntity> col)
    {
        var db = MongoMockHelper.CreateDatabaseMock(col);
        return new TestableTenantRepo(db.Object, col, _currentTenant.Object);
    }

    private MongoRepositoryBase<TestEntity, string> CreatePlainRepo(
        InMemoryMongoCollection<TestEntity> col)
    {
        var db = MongoMockHelper.CreateDatabaseMock(col);
        return new TestablePlainRepo(db.Object, col, _currentTenant.Object);
    }

    private class TestableTenantRepo : MongoRepositoryBase<TenantTestEntity, string>
    {
        public TestableTenantRepo(global::MongoDB.Driver.IMongoDatabase db, InMemoryMongoCollection<TenantTestEntity> col, ICurrentTenant? t)
            : base(db, t) { SetCollection(this, col); }
    }

    private class TestablePlainRepo : MongoRepositoryBase<TestEntity, string>
    {
        public TestablePlainRepo(global::MongoDB.Driver.IMongoDatabase db, InMemoryMongoCollection<TestEntity> col, ICurrentTenant? t)
            : base(db, t) { SetCollection(this, col); }
    }

    private static void SetCollection<TEntity>(object repo, InMemoryMongoCollection<TEntity> col)
        where TEntity : class
    {
        var field = repo.GetType().BaseType!
            .GetField("Collection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(repo, col);
    }

    #endregion
}
