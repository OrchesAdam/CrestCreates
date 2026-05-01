using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.OrmProviders.SqlSugar.Repositories;
using SqlSugar;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class SqlSugarConcurrencyTests : IDisposable
{
    private readonly ISqlSugarClient _sqlSugarClient;
    private readonly TestSqlSugarRepository _repository;

    public SqlSugarConcurrencyTests()
    {
        _sqlSugarClient = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = "DataSource=:memory:",
            IsAutoCloseConnection = false
        });
        _sqlSugarClient.Ado.Open();
        _sqlSugarClient.CodeFirst.InitTables<TestConcurrentEntity>();
        _repository = new TestSqlSugarRepository(_sqlSugarClient);
    }

    public void Dispose()
    {
        _sqlSugarClient?.Ado.Close();
        _sqlSugarClient?.Dispose();
    }

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

    private class TestSqlSugarRepository : SqlSugarRepository<TestConcurrentEntity, Guid>
    {
        public TestSqlSugarRepository(ISqlSugarClient sqlSugarClient) : base(sqlSugarClient, null) { }

        public override IQueryable<TestConcurrentEntity> GetQueryableUnfiltered()
        {
            throw new NotImplementedException();
        }
    }

    [SugarTable("TestConcurrentEntity")]
    private class TestConcurrentEntity : IEntity<Guid>, IHasConcurrencyStamp
    {
        public TestConcurrentEntity() { }
        public TestConcurrentEntity(Guid id) { Id = id; }

        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
    }
}
