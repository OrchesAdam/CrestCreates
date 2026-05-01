using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Exceptions;
using CrestCreates.OrmProviders.FreeSqlProvider.Repositories;
using CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork;
using FreeSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class FreeSqlConcurrencyTests : IDisposable
{
    private readonly IFreeSql _orm;
    private readonly TestFreeSqlRepository _repository;

    public FreeSqlConcurrencyTests()
    {
        _orm = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source=conc-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .UseAutoSyncStructure(true)
            .Build();
        _orm.CodeFirst.SyncStructure<TestConcurrentEntity>();
        _repository = new TestFreeSqlRepository(new FreeSqlUnitOfWorkManager(_orm),
            NullLogger<FreeSqlRepository<TestConcurrentEntity, Guid>>.Instance);
    }

    public void Dispose() => _orm.Dispose();

    [Fact]
    public async Task UpdateAsync_WithCorrectStamp_ShouldUpdateStamp()
    {
        var entity = new TestConcurrentEntity(Guid.NewGuid()) { Name = "Original" };
        await _repository.InsertAsync(entity);

        var oldStamp = entity.ConcurrencyStamp;
        entity.Name = "Updated";
        var result = await _repository.UpdateAsync(entity);

        Assert.NotNull(result);
        Assert.NotEqual(oldStamp, result.ConcurrencyStamp);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        var entity = new TestConcurrentEntity(Guid.NewGuid()) { Name = "Original" };
        await _repository.InsertAsync(entity);
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();
        entity.Name = "Updated";
        await Assert.ThrowsAsync<CrestConcurrencyException>(() => _repository.UpdateAsync(entity));
    }

    [Fact]
    public async Task DeleteAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        var entity = new TestConcurrentEntity(Guid.NewGuid()) { Name = "ToDelete" };
        await _repository.InsertAsync(entity);
        await Assert.ThrowsAsync<CrestConcurrencyException>(
            () => _repository.DeleteAsync(entity.Id, Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task DeleteAsync_WithCorrectStamp_ShouldSucceed()
    {
        var entity = new TestConcurrentEntity(Guid.NewGuid()) { Name = "ToDelete" };
        await _repository.InsertAsync(entity);
        await _repository.DeleteAsync(entity.Id, entity.ConcurrencyStamp);
        var deleted = await _repository.GetAsync(entity.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task UpdateRangeAsync_WithConcurrentEntity_ShouldThrowNotSupported()
    {
        var entities = new[] {
            new TestConcurrentEntity(Guid.NewGuid()) { Name = "A" },
            new TestConcurrentEntity(Guid.NewGuid()) { Name = "B" }
        };
        await Assert.ThrowsAsync<NotSupportedException>(() => _repository.UpdateRangeAsync(entities));
    }

    private class TestFreeSqlRepository : FreeSqlRepository<TestConcurrentEntity, Guid>
    {
        public TestFreeSqlRepository(FreeSqlUnitOfWorkManager uow, ILogger<FreeSqlRepository<TestConcurrentEntity, Guid>> logger) : base(uow, logger) { }

        public override IQueryable<TestConcurrentEntity> GetQueryableUnfiltered()
        {
            throw new NotImplementedException();
        }
    }

    private class TestConcurrentEntity : AuditedEntity<Guid>
    {
        public TestConcurrentEntity() { }
        public TestConcurrentEntity(Guid id) { Id = id; }
        public string Name { get; set; } = "";
    }
}
