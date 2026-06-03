using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.MongoDB.Tests.Helpers;
using CrestCreates.MongoDB.Tests.TestEntities;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.MongoDB.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.MongoDB.Tests;

public class MongoRepositoryBaseTests
{
    private readonly Mock<ICurrentTenant> _currentTenant = new();

    public MongoRepositoryBaseTests()
    {
        _currentTenant.Setup(t => t.Id).Returns(string.Empty);
    }

    [Fact]
    public async Task InsertAsync_ShouldAddEntity()
    {
        var entities = new List<TestEntity>();
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        var entity = new TestEntity { Name = "Test", Value = 42 };
        var result = await sut.InsertAsync(entity);

        result.Name.Should().Be("Test");
        entities.Should().ContainSingle();
    }

    [Fact]
    public async Task InsertRangeAsync_ShouldAddAll()
    {
        var entities = new List<TestEntity>();
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        var items = new List<TestEntity>
        {
            new() { Name = "A", Value = 1 },
            new() { Name = "B", Value = 2 }
        };
        var result = await sut.InsertRangeAsync(items);

        result.Should().HaveCount(2);
        entities.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAsync_ById_ShouldReturnEntity()
    {
        var entities = new List<TestEntity> { new() { Id = "abc", Name = "Found" } };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        var result = await sut.GetAsync("abc");
        result!.Name.Should().Be("Found");
    }

    [Fact]
    public async Task GetAsync_ById_ShouldReturnNull_WhenMissing()
    {
        var collection = MongoMockHelper.CreateCollection<TestEntity>();
        var sut = CreateRepo(collection);

        var result = await sut.GetAsync("nope");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetListAsync_ShouldReturnAll()
    {
        var entities = new List<TestEntity>
        {
            new() { Name = "A" }, new() { Name = "B" }
        };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        var result = await sut.GetListAsync();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReplace()
    {
        var entities = new List<TestEntity>();
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        var entity = new TestEntity { Id = "1", Name = "Updated" };
        var result = await sut.UpdateAsync(entity);
        result.Should().Be(entity);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemove()
    {
        var entities = new List<TestEntity> { new() { Id = "1", Name = "Del" } };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        await sut.DeleteAsync(entities[0]);
        entities.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ById_ShouldRemove()
    {
        var entities = new List<TestEntity> { new() { Id = "x", Name = "Gone" } };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        await sut.DeleteAsync("x");
        entities.Should().BeEmpty();
    }

    [Fact]
    public async Task SoftDelete_ShouldMarkIsDeleted()
    {
        var entities = new List<SoftDeleteTestEntity>();
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateSoftDeleteRepo(collection);

        var entity = new SoftDeleteTestEntity { Id = "sd-1", Name = "Soft" };
        await sut.DeleteAsync(entity);

        entity.IsDeleted.Should().BeTrue();
        entity.DeletionTime.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPagedAsync_ShouldReturnPagedResult()
    {
        var entities = new List<TestEntity>
        {
            new() { Name = "A", Value = 1 },
            new() { Name = "B", Value = 2 },
            new() { Name = "C", Value = 3 }
        };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        var result = await sut.GetPagedAsync(1, 10);
        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenExists()
    {
        var entities = new List<TestEntity> { new() { Id = "e1" } };
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateRepo(collection);

        (await sut.ExistsAsync("e1")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenMissing()
    {
        var collection = MongoMockHelper.CreateCollection<TestEntity>();
        var sut = CreateRepo(collection);

        (await sut.ExistsAsync("no")).Should().BeFalse();
    }

    [Fact]
    public async Task Insert_ShouldSetCreationAudit()
    {
        var entities = new List<TenantTestEntity>();
        var collection = MongoMockHelper.CreateCollection(entities);
        var sut = CreateTenantRepo(collection);

        var entity = new TenantTestEntity { Name = "Audited" };
        await sut.InsertAsync(entity);

        entity.CreationTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #region Helpers

    private MongoRepositoryBase<TestEntity, string> CreateRepo(
        InMemoryMongoCollection<TestEntity> col)
    {
        var db = MongoMockHelper.CreateDatabaseMock(col);
        return new TestableRepo(db.Object, col, _currentTenant.Object);
    }

    private MongoRepositoryBase<TenantTestEntity, string> CreateTenantRepo(
        InMemoryMongoCollection<TenantTestEntity> col)
    {
        var db = MongoMockHelper.CreateDatabaseMock(col);
        return new TestableTenantRepo(db.Object, col, _currentTenant.Object);
    }

    private MongoRepositoryBase<SoftDeleteTestEntity, string> CreateSoftDeleteRepo(
        InMemoryMongoCollection<SoftDeleteTestEntity> col)
    {
        var db = MongoMockHelper.CreateDatabaseMock(col);
        return new TestableSoftDeleteRepo(db.Object, col, _currentTenant.Object);
    }

    private class TestableRepo : MongoRepositoryBase<TestEntity, string>
    {
        public TestableRepo(global::MongoDB.Driver.IMongoDatabase db, InMemoryMongoCollection<TestEntity> col, ICurrentTenant? t)
            : base(db, t) { SetCollection(col); }
    }

    private class TestableTenantRepo : MongoRepositoryBase<TenantTestEntity, string>
    {
        public TestableTenantRepo(global::MongoDB.Driver.IMongoDatabase db, InMemoryMongoCollection<TenantTestEntity> col, ICurrentTenant? t)
            : base(db, t) { SetCollection(col); }
    }

    private class TestableSoftDeleteRepo : MongoRepositoryBase<SoftDeleteTestEntity, string>
    {
        public TestableSoftDeleteRepo(global::MongoDB.Driver.IMongoDatabase db, InMemoryMongoCollection<SoftDeleteTestEntity> col, ICurrentTenant? t)
            : base(db, t) { SetCollection(col); }
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
