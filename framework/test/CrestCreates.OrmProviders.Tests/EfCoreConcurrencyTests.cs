using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Extensions;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class EfCoreConcurrencyTests : IDisposable
{
    private readonly TestConcurrencyDbContext _dbContext;
    private readonly EfCoreRepository<TestConcurrentEntity, Guid> _repository;

    public EfCoreConcurrencyTests()
    {
        var options = new DbContextOptionsBuilder<TestConcurrencyDbContext>()
            .UseInMemoryDatabase($"conc-{Guid.NewGuid():N}")
            .Options;
        _dbContext = new TestConcurrencyDbContext(options);
        _repository = new EfCoreRepository<TestConcurrentEntity, Guid>(new EfCoreDbContextAdapter(_dbContext));
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task UpdateAsync_WithCorrectStamp_ShouldSucceed()
    {
        var entity = new TestConcurrentEntity(Guid.NewGuid()) { Name = "Original" };
        _dbContext.TestEntities.Add(entity);
        await _dbContext.SaveChangesAsync();
        _dbContext.Entry(entity).State = EntityState.Detached;

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
        _dbContext.TestEntities.Add(entity);
        await _dbContext.SaveChangesAsync();
        _dbContext.Entry(entity).State = EntityState.Detached;

        entity.ConcurrencyStamp = Guid.NewGuid().ToString();
        entity.Name = "Updated";

        await Assert.ThrowsAsync<CrestConcurrencyException>(() => _repository.UpdateAsync(entity));
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
}

public class TestConcurrentEntity : AuditedEntity<Guid>
{
    public TestConcurrentEntity() { }
    public TestConcurrentEntity(Guid id) { Id = id; }
    public string Name { get; set; } = "";
}

public class TestConcurrencyDbContext : DbContext
{
    public DbSet<TestConcurrentEntity> TestEntities => Set<TestConcurrentEntity>();

    public TestConcurrencyDbContext(DbContextOptions<TestConcurrencyDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TestConcurrentEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });
        modelBuilder.ConfigureConcurrencyStamp();
    }
}
