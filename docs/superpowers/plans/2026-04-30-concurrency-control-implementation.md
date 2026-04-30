# Concurrency Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unified optimistic concurrency control (ConcurrencyStamp) across AuditedEntity/AuditedAggregateRoot, all three ORMs, with client-driven If-Match for DELETE.

**Architecture:** `IHasConcurrencyStamp` interface on AuditedEntity base → repository WHERE-checks the old stamp on Update/Delete → `CrestConcurrencyException` on 0 rows. EF Core uses `IsConcurrencyToken()` + `OriginalValue`; FreeSql/SqlSugar use string-based WHERE. Generated CRUD services enforce If-Match on DELETE for concurrent entities.

**Tech Stack:** .NET 10, EF Core, FreeSql, SqlSugar, Roslyn Source Generators, xUnit

---

### Task 1: IHasConcurrencyStamp Interface

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/Entities/Auditing/IHasConcurrencyStamp.cs`
- Modify: `framework/src/CrestCreates.Domain/Entities/Auditing/AuditedEntity.cs`
- Modify: `framework/src/CrestCreates.Domain/Entities/Auditing/AuditedAggregateRoot.cs`
- Test: `framework/test/CrestCreates.Domain.Tests/ConcurrencyStampTests.cs`

- [ ] **Step 1: Write the failing test**

Create `framework/test/CrestCreates.Domain.Tests/ConcurrencyStampTests.cs`:

```csharp
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Xunit;

namespace CrestCreates.Domain.Tests;

public class ConcurrencyStampTests
{
    [Fact]
    public void NewAuditedEntity_HasNonEmptyConcurrencyStamp()
    {
        var entity = new TestAuditedEntity();
        Assert.False(string.IsNullOrEmpty(entity.ConcurrencyStamp));
    }

    [Fact]
    public void NewAuditedEntity_ConcurrencyStamp_IsValidGuid()
    {
        var entity = new TestAuditedEntity();
        Assert.True(Guid.TryParse(entity.ConcurrencyStamp, out _));
    }

    [Fact]
    public void AuditedEntity_ImplementsIHasConcurrencyStamp()
    {
        var entity = new TestAuditedEntity();
        Assert.IsAssignableFrom<IHasConcurrencyStamp>(entity);
    }

    [Fact]
    public void ConcurrencyStamp_CanBeSet()
    {
        var entity = new TestAuditedEntity();
        entity.ConcurrencyStamp = "custom-stamp";
        Assert.Equal("custom-stamp", entity.ConcurrencyStamp);
    }

    private class TestAuditedEntity : AuditedEntity<Guid> { }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test framework/test/CrestCreates.Domain.Tests --filter "FullyQualifiedName~ConcurrencyStampTests"
```

Expected: FAIL — `IHasConcurrencyStamp` not defined, `ConcurrencyStamp` property doesn't exist.

- [ ] **Step 3: Create IHasConcurrencyStamp interface**

Create `framework/src/CrestCreates.Domain.Shared/Entities/Auditing/IHasConcurrencyStamp.cs`:

```csharp
namespace CrestCreates.Domain.Shared.Entities.Auditing;

public interface IHasConcurrencyStamp
{
    string ConcurrencyStamp { get; set; }
}
```

- [ ] **Step 4: Add ConcurrencyStamp to AuditedEntity**

Edit `framework/src/CrestCreates.Domain/Entities/Auditing/AuditedEntity.cs`:

Current:
```csharp
using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing
{
    public abstract class AuditedEntity<TId> : Entity<TId>, IAuditedEntity<TId> where TId : IEquatable<TId>
    {
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
    }
}
```

Change to:
```csharp
using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing
{
    public abstract class AuditedEntity<TId> : Entity<TId>, IAuditedEntity<TId>, IHasConcurrencyStamp where TId : IEquatable<TId>
    {
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
        public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
    }
}
```

- [ ] **Step 5: Add ConcurrencyStamp to AuditedAggregateRoot**

Edit `framework/src/CrestCreates.Domain/Entities/Auditing/AuditedAggregateRoot.cs`:

Current:
```csharp
using System;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing;

public abstract class AuditedAggregateRoot<TId> : AggregateRoot<TId>, IAuditedEntity<TId> where TId : IEquatable<TId>
{
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
}
```

Change to:
```csharp
using System;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.Domain.Entities.Auditing;

public abstract class AuditedAggregateRoot<TId> : AggregateRoot<TId>, IAuditedEntity<TId>, IHasConcurrencyStamp where TId : IEquatable<TId>
{
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}
```

- [ ] **Step 6: Run test to verify it passes**

```bash
dotnet test framework/test/CrestCreates.Domain.Tests --filter "FullyQualifiedName~ConcurrencyStampTests"
```

Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Domain.Shared/Entities/Auditing/IHasConcurrencyStamp.cs framework/src/CrestCreates.Domain/Entities/Auditing/AuditedEntity.cs framework/src/CrestCreates.Domain/Entities/Auditing/AuditedAggregateRoot.cs framework/test/CrestCreates.Domain.Tests/ConcurrencyStampTests.cs
git commit -m "feat: add IHasConcurrencyStamp interface and ConcurrencyStamp property to AuditedEntity/AuditedAggregateRoot"
```

---

### Task 2: Exception Classes

**Files:**
- Create: `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs`
- Create: `framework/src/CrestCreates.Domain/Exceptions/CrestPreconditionRequiredException.cs`
- Test: `framework/test/CrestCreates.Domain.Tests/ConcurrencyExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `framework/test/CrestCreates.Domain.Tests/ConcurrencyExceptionTests.cs`:

```csharp
using CrestCreates.Domain.Exceptions;
using Xunit;

namespace CrestCreates.Domain.Tests;

public class ConcurrencyExceptionTests
{
    [Fact]
    public void CrestConcurrencyException_HasEntityTypeAndId()
    {
        var ex = new CrestConcurrencyException("Book", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Equal("Book", ex.EntityType);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), ex.EntityId);
        Assert.Contains("Book", ex.Message);
        Assert.Contains("concurrency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrestPreconditionRequiredException_HasEntityTypeAndId()
    {
        var ex = new CrestPreconditionRequiredException("Book", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        Assert.Equal("Book", ex.EntityType);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), ex.EntityId);
        Assert.Contains("If-Match", ex.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test framework/test/CrestCreates.Domain.Tests --filter "FullyQualifiedName~ConcurrencyExceptionTests"
```

Expected: FAIL — types don't exist.

- [ ] **Step 3: Create CrestConcurrencyException**

Create `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs`:

```csharp
using System;

namespace CrestCreates.Domain.Exceptions;

public class CrestConcurrencyException : Exception
{
    public string EntityType { get; }
    public object? EntityId { get; }

    public CrestConcurrencyException(string entityType, object? entityId)
        : base($"Concurrency conflict: {entityType} (Id={entityId}) has been modified by another user.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
```

- [ ] **Step 4: Create CrestPreconditionRequiredException**

Create `framework/src/CrestCreates.Domain/Exceptions/CrestPreconditionRequiredException.cs`:

```csharp
using System;

namespace CrestCreates.Domain.Exceptions;

public class CrestPreconditionRequiredException : Exception
{
    public string EntityType { get; }
    public object? EntityId { get; }

    public CrestPreconditionRequiredException(string entityType, object? entityId)
        : base($"Precondition required: DELETE on {entityType} (Id={entityId}) requires If-Match header with current ConcurrencyStamp.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test framework/test/CrestCreates.Domain.Tests --filter "FullyQualifiedName~ConcurrencyExceptionTests"
```

Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs framework/src/CrestCreates.Domain/Exceptions/CrestPreconditionRequiredException.cs framework/test/CrestCreates.Domain.Tests/ConcurrencyExceptionTests.cs
git commit -m "feat: add CrestConcurrencyException and CrestPreconditionRequiredException"
```

---

### Task 3: Repository Interface — new DeleteAsync(id, expectedStamp) overload

**Files:**
- Modify: `framework/src/CrestCreates.Domain/Repositories/ICrestRepositoryBase.cs`
- Modify: `framework/src/CrestCreates.Domain/Repositories/CrestRepositoryBase.cs`

- [ ] **Step 1: Add to ICrestRepositoryBase**

Edit `framework/src/CrestCreates.Domain/Repositories/ICrestRepositoryBase.cs`, add after line 35 (`DeleteAsync(TKey id, CancellationToken cancellationToken = default)`):

```csharp
    Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Add to CrestRepositoryBase**

Edit `framework/src/CrestCreates.Domain/Repositories/CrestRepositoryBase.cs`, add after line 61 (`DeleteAsync(TKey id, CancellationToken cancellationToken = default)`):

```csharp
    public abstract Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Build to verify no compilation errors in dependent projects**

```bash
dotnet build framework/src/CrestCreates.Domain
```

Expected: PASS (abstractions only — ORM implementations will show errors until Task 4/6/7).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Domain/Repositories/ICrestRepositoryBase.cs framework/src/CrestCreates.Domain/Repositories/CrestRepositoryBase.cs
git commit -m "feat: add DeleteAsync(id, expectedStamp) overload to ICrestRepositoryBase and CrestRepositoryBase"
```

---

### Task 4: EF Core — UpdateAsync, DeleteAsync, UpdateRangeAsync + Model Config

**Files:**
- Create: `framework/src/CrestCreates.OrmProviders.EFCore/Extensions/ModelBuilderExtensions.cs`
- Modify: `framework/src/CrestCreates.OrmProviders.EFCore/DbContexts/CrestCreatesDbContext.cs`
- Modify: `framework/src/CrestCreates.OrmProviders.EFCore/Repositories/EfCoreRepository.cs`
- Modify: `framework/src/CrestCreates.OrmProviders.EFCore/Repositories/EfCoreRepositoryBase.cs`
- Test: `framework/test/CrestCreates.OrmProviders.Tests/EfCoreConcurrencyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `framework/test/CrestCreates.OrmProviders.Tests/EfCoreConcurrencyTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class EfCoreConcurrencyTests : IDisposable
{
    private readonly CrestCreatesDbContext _dbContext;
    private readonly EfCoreRepository<TestConcurrentEntity, Guid> _repository;

    public EfCoreConcurrencyTests()
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"conc-{Guid.NewGuid():N}")
            .Options;

        _dbContext = new CrestCreatesDbContext(options);
        _repository = new EfCoreRepository<TestConcurrentEntity, Guid>(
            new EfCoreDbContextAdapter(_dbContext));
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task UpdateAsync_WithCorrectStamp_ShouldSucceed()
    {
        // Arrange
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "Original" };
        _dbContext.Set<TestConcurrentEntity>().Add(entity);
        await _dbContext.SaveChangesAsync();

        // Detach so we can re-attach with a modified entity
        _dbContext.Entry(entity).State = EntityState.Detached;

        var oldStamp = entity.ConcurrencyStamp;
        entity.Name = "Updated";
        // Stamp is still oldStamp (expected stamp from "client")

        // Act
        var result = await _repository.UpdateAsync(entity);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(oldStamp, result.ConcurrencyStamp);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        // Arrange
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "Original" };
        _dbContext.Set<TestConcurrentEntity>().Add(entity);
        await _dbContext.SaveChangesAsync();

        _dbContext.Entry(entity).State = EntityState.Detached;

        entity.ConcurrencyStamp = Guid.NewGuid().ToString(); // stale/wrong stamp
        entity.Name = "Updated";

        // Act & Assert
        await Assert.ThrowsAsync<CrestConcurrencyException>(
            () => _repository.UpdateAsync(entity));
    }

    [Fact]
    public async Task DeleteAsync_WithCorrectStamp_ShouldSucceed()
    {
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "ToDelete" };
        _dbContext.Set<TestConcurrentEntity>().Add(entity);
        await _dbContext.SaveChangesAsync();

        await _repository.DeleteAsync(entity.Id, entity.ConcurrencyStamp);

        var exists = await _dbContext.Set<TestConcurrentEntity>().FindAsync(entity.Id);
        Assert.Null(exists);
    }

    [Fact]
    public async Task DeleteAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "ToDelete" };
        _dbContext.Set<TestConcurrentEntity>().Add(entity);
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<CrestConcurrencyException>(
            () => _repository.DeleteAsync(entity.Id, Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task UpdateRangeAsync_WithConcurrentEntity_ShouldThrowNotSupported()
    {
        var entities = new[]
        {
            new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "A" },
            new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "B" }
        };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _repository.UpdateRangeAsync(entities));
    }

    private class TestConcurrentEntity : AuditedEntity<Guid> { public string Name { get; set; } = ""; }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~EfCoreConcurrencyTests"
```

Expected: FAIL — repository doesn't have concurrency logic yet.

- [ ] **Step 3: Create ConfigureConcurrencyStamp extension**

Create `framework/src/CrestCreates.OrmProviders.EFCore/Extensions/ModelBuilderExtensions.cs`:

```csharp
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Extensions;

public static class ModelBuilderExtensions
{
    public static ModelBuilder ConfigureConcurrencyStamp(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IHasConcurrencyStamp).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp))
                    .IsConcurrencyToken();
            }
        }
        return modelBuilder;
    }
}
```

- [ ] **Step 4: Call ConfigureConcurrencyStamp in CrestCreatesDbContext**

Edit `framework/src/CrestCreates.OrmProviders.EFCore/DbContexts/CrestCreatesDbContext.cs`. Add after existing usings:

```csharp
using CrestCreates.OrmProviders.EFCore.Extensions;
```

Add at the end of `OnModelCreating` (before the closing brace of the method, after the tenant discriminator block):

```csharp
            modelBuilder.ConfigureConcurrencyStamp();
```

- [ ] **Step 5: Implement concurrency in EfCoreRepository.UpdateAsync**

Edit `framework/src/CrestCreates.OrmProviders.EFCore/Repositories/EfCoreRepository.cs`. Replace the `UpdateAsync` method:

```csharp
        public override async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            if (entity is IHasConcurrencyStamp)
            {
                var entry = _dbContext.Set<TEntity>().Update(entity);
                entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue = entity.ConcurrencyStamp;
                entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).IsModified = true;
            }
            else
            {
                _dbContext.Set<TEntity>().Update(entity);
            }

            try
            {
                await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new CrestConcurrencyException(typeof(TEntity).Name, ((dynamic)entity).Id);
            }

            return entity;
        }
```

Add usings at top:
```csharp
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.EntityFrameworkCore;
```

- [ ] **Step 6: Implement concurrency in EfCoreRepository.DeleteAsync(new overload)**

Edit `framework/src/CrestCreates.OrmProviders.EFCore/Repositories/EfCoreRepository.cs`. Add the new `DeleteAsync(id, expectedStamp)`:

```csharp
        public override async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            var rows = await _dbContext.Set<TEntity>()
                .Where(e => e.Id.Equals(id)
                    && EF.Property<string>(e, "ConcurrencyStamp") == expectedStamp)
                .ExecuteDeleteAsync(cancellationToken);
            if (rows == 0)
                throw new CrestConcurrencyException(typeof(TEntity).Name, id);
        }
```

- [ ] **Step 7: Implement concurrency in EfCoreRepositoryBase.UpdateAsync**

Edit `framework/src/CrestCreates.OrmProviders.EFCore/Repositories/EfCoreRepositoryBase.cs`. Same pattern as EfCoreRepository — replace `UpdateAsync` with the concurrency-aware version shown in Step 5.

Add usings:
```csharp
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.EntityFrameworkCore;
```

- [ ] **Step 8: Implement concurrency in EfCoreRepositoryBase.DeleteAsync(new overload)**

Edit `framework/src/CrestCreates.OrmProviders.EFCore/Repositories/EfCoreRepositoryBase.cs`. Same implementation as Step 6.

- [ ] **Step 9: Implement UpdateRangeAsync guard in EfCoreRepository**

Edit `framework/src/CrestCreates.OrmProviders.EFCore/Repositories/EfCoreRepository.cs`. Add guard at the start of `UpdateRangeAsync`:

```csharp
        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();
            if (entityList.Any(e => e is IHasConcurrencyStamp))
            {
                throw new NotSupportedException(
                    "UpdateRangeAsync does not support entities with IHasConcurrencyStamp. Update concurrent entities individually via UpdateAsync.");
            }
            _dbContext.Set<TEntity>().UpdateRange(entityList);
            await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            return entityList;
        }
```

- [ ] **Step 10: Implement UpdateRangeAsync guard in EfCoreRepositoryBase**

Same as Step 9 for `EfCoreRepositoryBase.UpdateRangeAsync`.

- [ ] **Step 11: Implement DeleteAsync(entity) concurrency in EfCoreRepository**

Edit the existing `DeleteAsync(TEntity entity)` in `EfCoreRepository.cs`:

```csharp
        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            if (entity is IHasConcurrencyStamp)
            {
                var entry = _dbContext.Set<TEntity>().Remove(entity);
                entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue = entity.ConcurrencyStamp;
            }
            else
            {
                _dbContext.Set<TEntity>().Remove(entity);
            }

            try
            {
                await SaveChangesIfNoActiveTransactionAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new CrestConcurrencyException(typeof(TEntity).Name, ((dynamic)entity).Id);
            }
        }
```

- [ ] **Step 12: Run test to verify all pass**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~EfCoreConcurrencyTests"
```

Expected: PASS (5 tests). Note: InMemoryDatabase doesn't support `ExecuteDeleteAsync` — you may get an error. If so, adjust the test to not use InMemory for the DeleteAsync(id, stamp) test, or mark with `[Fact(Skip = "ExecuteDeleteAsync not supported by InMemory")]` and cover via integration tests.

- [ ] **Step 13: Commit**

```bash
git add framework/src/CrestCreates.OrmProviders.EFCore/
git add framework/test/CrestCreates.OrmProviders.Tests/EfCoreConcurrencyTests.cs
git commit -m "feat: implement EF Core concurrency — UpdateAsync, DeleteAsync, UpdateRangeAsync + ConfigureConcurrencyStamp"
```

---

### Task 5: FreeSql — UpdateAsync, DeleteAsync, UpdateRangeAsync

**Files:**
- Modify: `framework/src/CrestCreates.OrmProviders.FreeSqlProvider/Repositories/FreeSqlRepositoryBase.cs`
- Test: `framework/test/CrestCreates.OrmProviders.Tests/FreeSqlConcurrencyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `framework/test/CrestCreates.OrmProviders.Tests/FreeSqlConcurrencyTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.OrmProviders.FreeSqlProvider.Repositories;
using FreeSql;
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
            .UseConnectionString(DataType.Sqlite, $"Data Source=conc-{Guid.NewGuid():N}.db;Mode=Memory;Cache=Shared")
            .UseAutoSyncStructure(true)
            .Build();
        _repository = new TestFreeSqlRepository(
            new FreeSqlUnitOfWorkManager(_orm),
            NullLogger<FreeSqlRepository<TestConcurrentEntity, Guid>>.Instance);
    }

    public void Dispose() => _orm.Dispose();

    [Fact]
    public async Task UpdateAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "Original" };
        await _repository.InsertAsync(entity);

        entity.ConcurrencyStamp = Guid.NewGuid().ToString(); // stale
        entity.Name = "Updated";

        await Assert.ThrowsAsync<CrestConcurrencyException>(
            () => _repository.UpdateAsync(entity));
    }

    [Fact]
    public async Task DeleteAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "ToDelete" };
        await _repository.InsertAsync(entity);

        await Assert.ThrowsAsync<CrestConcurrencyException>(
            () => _repository.DeleteAsync(entity.Id, Guid.NewGuid().ToString()));
    }

    private class TestFreeSqlRepository : FreeSqlRepository<TestConcurrentEntity, Guid>
    {
        public TestFreeSqlRepository(FreeSqlUnitOfWorkManager uow, ILogger<FreeSqlRepository<TestConcurrentEntity, Guid>> logger)
            : base(uow, logger) { }
    }

    private class TestConcurrentEntity : AuditedEntity<Guid> { public string Name { get; set; } = ""; }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~FreeSqlConcurrencyTests"
```

Expected: FAIL.

- [ ] **Step 3: Implement concurrency in FreeSqlRepositoryBase.UpdateAsync**

Edit `framework/src/CrestCreates.OrmProviders.FreeSqlProvider/Repositories/FreeSqlRepositoryBase.cs`. Replace `UpdateAsync`:

```csharp
        public override async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            if (entity is IHasConcurrencyStamp stamp)
            {
                var oldStamp = stamp.ConcurrencyStamp;
                stamp.ConcurrencyStamp = Guid.NewGuid().ToString();

                var rows = await _orm.Update<TEntity>()
                    .SetSource(entity)
                    .Where("Id = {0} AND ConcurrencyStamp = {1}", entity.Id, oldStamp)
                    .ExecuteAffrowsAsync(cancellationToken);

                if (rows == 0)
                    throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);

                return entity;
            }
            else
            {
                await _orm.Update<TEntity>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);
                return entity;
            }
        }
```

Add usings:
```csharp
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
```

- [ ] **Step 4: Implement DeleteAsync(id, expectedStamp) and UpdateRangeAsync**

In the same file, add `DeleteAsync(TKey id, string expectedStamp, ...)`:

```csharp
        public override async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            var rows = await _orm.Delete<TEntity>()
                .Where("Id = {0} AND ConcurrencyStamp = {1}", id, expectedStamp)
                .ExecuteAffrowsAsync(cancellationToken);

            if (rows == 0)
                throw new CrestConcurrencyException(typeof(TEntity).Name, id);
        }
```

Add UpdateRangeAsync guard:

```csharp
        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();
            if (entityList.Any(e => e is IHasConcurrencyStamp))
            {
                throw new NotSupportedException(
                    "UpdateRangeAsync does not support entities with IHasConcurrencyStamp. Update concurrent entities individually via UpdateAsync.");
            }
            await _orm.Update<TEntity>().SetSource(entityList).ExecuteAffrowsAsync(cancellationToken);
            return entityList;
        }
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~FreeSqlConcurrencyTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.OrmProviders.FreeSqlProvider/Repositories/FreeSqlRepositoryBase.cs
git add framework/test/CrestCreates.OrmProviders.Tests/FreeSqlConcurrencyTests.cs
git commit -m "feat: implement FreeSql concurrency — UpdateAsync, DeleteAsync, UpdateRangeAsync"
```

---

### Task 6: SqlSugar — UpdateAsync, DeleteAsync, UpdateRangeAsync

**Files:**
- Modify: `framework/src/CrestCreates.OrmProviders.SqlSugar/Repositories/SqlSugarRepository.cs`
- Test: `framework/test/CrestCreates.OrmProviders.Tests/SqlSugarConcurrencyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `framework/test/CrestCreates.OrmProviders.Tests/SqlSugarConcurrencyTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.OrmProviders.SqlSugar.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class SqlSugarConcurrencyTests : IDisposable
{
    private readonly SqlSugarScope _client;
    private readonly TestSqlSugarRepository _repository;

    public SqlSugarConcurrencyTests()
    {
        _client = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = $"DataSource=conc-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true
        });
        _client.DbMaintenance.CreateDatabase();
        _client.CodeFirst.InitTables<TestConcurrentEntity>();
        _repository = new TestSqlSugarRepository(
            _client,
            NullLogger<SqlSugarRepository<TestConcurrentEntity, Guid>>.Instance);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task UpdateAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "Original" };
        await _repository.InsertAsync(entity);

        entity.ConcurrencyStamp = Guid.NewGuid().ToString(); // stale
        entity.Name = "Updated";

        await Assert.ThrowsAsync<CrestConcurrencyException>(
            () => _repository.UpdateAsync(entity));
    }

    [Fact]
    public async Task DeleteAsync_WithStaleStamp_ShouldThrowConcurrencyException()
    {
        var entity = new TestConcurrentEntity { Id = Guid.NewGuid(), Name = "ToDelete" };
        await _repository.InsertAsync(entity);

        await Assert.ThrowsAsync<CrestConcurrencyException>(
            () => _repository.DeleteAsync(entity.Id, Guid.NewGuid().ToString()));
    }

    private class TestSqlSugarRepository : SqlSugarRepository<TestConcurrentEntity, Guid>
    {
        public TestSqlSugarRepository(ISqlSugarClient client, ILogger<SqlSugarRepository<TestConcurrentEntity, Guid>> logger)
            : base(client, logger) { }
    }

    public class TestConcurrentEntity : AuditedEntity<Guid> { public string Name { get; set; } = ""; }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~SqlSugarConcurrencyTests"
```

Expected: FAIL.

- [ ] **Step 3: Implement concurrency in SqlSugarRepository.UpdateAsync**

Edit `framework/src/CrestCreates.OrmProviders.SqlSugar/Repositories/SqlSugarRepository.cs`. Replace `UpdateAsync`:

```csharp
        public override async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            if (entity is IHasConcurrencyStamp stamp)
            {
                var oldStamp = stamp.ConcurrencyStamp;
                stamp.ConcurrencyStamp = Guid.NewGuid().ToString();

                var rows = await _sqlSugarClient.Updateable(entity)
                    .IgnoreColumns("DomainEvents")
                    .Where("Id = @Id AND ConcurrencyStamp = @OldStamp",
                           new { Id = entity.Id, OldStamp = oldStamp })
                    .ExecuteCommandAsync();

                if (rows == 0)
                    throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);

                return entity;
            }
            else
            {
                await _sqlSugarClient.Updateable(entity).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
                return entity;
            }
        }
```

Add usings:
```csharp
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
```

- [ ] **Step 4: Implement DeleteAsync(id, expectedStamp) and UpdateRangeAsync**

In the same file, add `DeleteAsync(TKey id, string expectedStamp, ...)`:

```csharp
        public override async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            var rows = await _sqlSugarClient.Deleteable<TEntity>()
                .Where("Id = @Id AND ConcurrencyStamp = @Stamp",
                       new { Id = id, Stamp = expectedStamp })
                .ExecuteCommandAsync();

            if (rows == 0)
                throw new CrestConcurrencyException(typeof(TEntity).Name, id);
        }
```

Add UpdateRangeAsync guard:

```csharp
        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();
            if (entityList.Any(e => e is IHasConcurrencyStamp))
            {
                throw new NotSupportedException(
                    "UpdateRangeAsync does not support entities with IHasConcurrencyStamp. Update concurrent entities individually via UpdateAsync.");
            }
            await _sqlSugarClient.Updateable(entityList).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
            return entityList;
        }
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~SqlSugarConcurrencyTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.OrmProviders.SqlSugar/Repositories/SqlSugarRepository.cs
git add framework/test/CrestCreates.OrmProviders.Tests/SqlSugarConcurrencyTests.cs
git commit -m "feat: implement SqlSugar concurrency — UpdateAsync, DeleteAsync, UpdateRangeAsync"
```

---

### Task 7: Application Services — re-throw + Obsolete + ICrudAppService

**Files:**
- Modify: `framework/src/CrestCreates.Application/Services/CrestAppServiceBase.cs`
- Modify: `framework/src/CrestCreates.Application/Services/CrudServiceBase.cs`
- Modify: `framework/src/CrestCreates.Application/Services/ICrudService.cs`
- Modify: `framework/src/CrestCreates.Application.Contracts/Interfaces/ICrudAppService.cs`
- Test: `framework/test/CrestCreates.Application.Tests/CrudServiceConcurrencyTests.cs`

- [ ] **Step 1: Update ICrudAppService.DeleteAsync signature**

Edit `framework/src/CrestCreates.Application.Contracts/Interfaces/ICrudAppService.cs`. Change:

```csharp
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
```

To:

```csharp
    Task DeleteAsync(TKey id, string? expectedStamp = null, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Add CrestConcurrencyException and CrestPreconditionRequiredException re-throw in CrestAppServiceBase**

Edit `framework/src/CrestCreates.Application/Services/CrestAppServiceBase.cs`. In `UpdateAsync` catch block, add after `catch (KeyNotFoundException) { throw; }`:

```csharp
            catch (CrestConcurrencyException)
            {
                throw;
            }
            catch (CrestPreconditionRequiredException)
            {
                throw;
            }
```

Add using:
```csharp
using CrestCreates.Domain.Exceptions;
```

In `DeleteAsync`, add the same two re-throw blocks after `catch (KeyNotFoundException)` if one exists, or before `catch (Exception)`.

Both exception types must be re-thrown: `CrestConcurrencyException` (409 on stamp mismatch) and `CrestPreconditionRequiredException` (428 on missing If-Match). Without the re-throw, the catch-all `catch (Exception)` would wrap either into a generic 500 error, breaking the middleware's ability to map them correctly.

- [ ] **Step 3: Mark CrudServiceBase and ICrudService as Obsolete**

Edit `framework/src/CrestCreates.Application/Services/CrudServiceBase.cs`. Add before the class declaration:

```csharp
    [Obsolete("Use generated CRUD service or CrestAppServiceBase for concurrency support.")]
```

Edit `framework/src/CrestCreates.Application/Services/ICrudService.cs`. Add before the interface declaration:

```csharp
    [Obsolete("Use generated CRUD service or CrestAppServiceBase for concurrency support.")]
```

- [ ] **Step 4: Write tests**

Create `framework/test/CrestCreates.Application.Tests/CrudServiceConcurrencyTests.cs`:

```csharp
using CrestCreates.Application.Services;
using CrestCreates.Domain.Exceptions;
using Xunit;

namespace CrestCreates.Application.Tests;

public class CrudServiceConcurrencyTests
{
    [Fact]
    public void CrudServiceBase_IsMarkedObsolete()
    {
        var type = typeof(CrudServiceBase<,,,>);
        var attrs = type.GetCustomAttributes(typeof(ObsoleteAttribute), false);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public void CrudServiceBase_Delete_DoesNotHaveConcurrencyProtection()
    {
        // CrudServiceBase.DeleteAsync only takes TKey id — no expectedStamp parameter
        var method = typeof(CrudServiceBase<,,,>).GetMethod("DeleteAsync");
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        // Only has TKey id and optional CancellationToken — no string expectedStamp
        Assert.DoesNotContain(parameters, p => p.Name == "expectedStamp"
            && p.ParameterType == typeof(string));
    }
}
```

- [ ] **Step 5: Build and run tests**

```bash
dotnet build framework/src/CrestCreates.Application
dotnet test framework/test/CrestCreates.Application.Tests --filter "FullyQualifiedName~CrudServiceConcurrencyTests"
```

Expected: both tests PASS. Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Application/
git add framework/src/CrestCreates.Application.Contracts/
git add framework/test/CrestCreates.Application.Tests/
git commit -m "feat: add concurrency re-throw in CrestAppServiceBase, mark CrudServiceBase obsolete, add expectedStamp to ICrudAppService"
```

---

### Task 8: Middleware — 409 and 428 mapping

**Files:**
- Modify: `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs`

- [ ] **Step 1: Add exception handlers**

Edit `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs`. Add after the `case CrestPermissionException` block:

```csharp
                case CrestConcurrencyException concurrencyEx:
                    context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                    errorResponse.Code = (int)HttpStatusCode.Conflict;
                    errorResponse.Message = "数据已被其他用户修改，请刷新后重试";
                    errorResponse.Details = concurrencyEx.Message;
                    logger.LogWarning(concurrencyEx,
                        "Concurrency conflict for request {TraceId}", context.TraceIdentifier);
                    break;
                case CrestPreconditionRequiredException preEx:
                    context.Response.StatusCode = 428;
                    errorResponse.Code = 428;
                    errorResponse.Message = "请求缺少 If-Match 头，请提供当前 ConcurrencyStamp";
                    errorResponse.Details = preEx.Message;
                    logger.LogWarning(preEx,
                        "Precondition required for request {TraceId}", context.TraceIdentifier);
                    break;
```

Add using:
```csharp
using CrestCreates.Domain.Exceptions;
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.AspNetCore
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs
git commit -m "feat: add 409 and 428 exception handlers to middleware for concurrency"
```

---

### Task 9: Controller — If-Match header in CrudControllerBase

**Files:**
- Modify: `framework/src/CrestCreates.AspNetCore/Controllers/CrudControllerBase.cs`

- [ ] **Step 1: Update DeleteAsync method**

Edit `framework/src/CrestCreates.AspNetCore/Controllers/CrudControllerBase.cs`. Replace the `DeleteAsync` method:

```csharp
    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> DeleteAsync(
        [FromRoute] TKey id,
        [FromHeader(Name = "If-Match")] string? ifMatch = null,
        CancellationToken cancellationToken = default)
    {
        await Service.DeleteAsync(id, expectedStamp: ifMatch, cancellationToken);
        return NoContent();
    }
```

Add using if not already present:
```csharp
using Microsoft.AspNetCore.Mvc;
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.AspNetCore
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.AspNetCore/Controllers/CrudControllerBase.cs
git commit -m "feat: add If-Match header support to CrudControllerBase.DeleteAsync"
```

---

### Task 10: EntitySourceGenerator — exclude ConcurrencyStamp from CreateDto

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`

- [ ] **Step 1: Fix GenerateCreateEntityDto**

Edit `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`. At line ~1082, change the property filter:

```csharp
foreach (var prop in properties.Where(p => p.Name != "Id" && p.Name != "CreationTime" && p.Name != "LastModificationTime" && p.Name != "CreatorId" && p.Name != "LastModifierId"))
```

Add `&& p.Name != "ConcurrencyStamp"`:

```csharp
foreach (var prop in properties.Where(p => p.Name != "Id" && p.Name != "CreationTime" && p.Name != "LastModificationTime" && p.Name != "CreatorId" && p.Name != "LastModifierId" && p.Name != "ConcurrencyStamp"))
```

- [ ] **Step 2: Build the code generator**

```bash
dotnet build framework/tools/CrestCreates.CodeGenerator
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs
git commit -m "fix: exclude ConcurrencyStamp from generated CreateXxxDto"
```

---

### Task 11: CrudServiceSourceGenerator — include stamp in UpdateDto, stamped Delete, UoW

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

- [ ] **Step 1: Remove ConcurrencyStamp from UpdateDto exclusion**

At line ~311, the `defaultExcludedProperties` for `GenerateUpdateEntityDto` currently includes `"ConcurrencyStamp"`. Remove it:

Current (line 311):
```csharp
var defaultExcludedProperties = new[] { "Id", "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId", "IsDeleted", "DeletionTime", "DeleterId", "ConcurrencyStamp" };
```

Change to:
```csharp
var defaultExcludedProperties = new[] { "Id", "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId", "IsDeleted", "DeletionTime", "DeleterId" };
```

- [ ] **Step 2: Update generated UpdateAsync body**

At the generated UpdateAsync method body (around line ~580), ensure `ConcurrencyStamp` is mapped from input to entity. The existing mapping at line ~382 already filters ConcurrencyStamp in for string properties — ensure the logic maps `input.ConcurrencyStamp` to `entity.ConcurrencyStamp` before calling `_repository.UpdateAsync`.

Verify the generated code includes:
```csharp
entity.ConcurrencyStamp = input.ConcurrencyStamp;
```

This should happen automatically since ConcurrencyStamp is now in the UpdateDto and the string property mapping at line ~382 does `entity.Property = input.Property`. No code change needed here if the DTO generation already includes it — just verify.

- [ ] **Step 3: Generate stamped DeleteAsync signature**

The generated `DeleteAsync` method signature (around line 662) needs to change from:
```csharp
public virtual async Task DeleteAsync({idType} id, CancellationToken cancellationToken = default)
```

To:
```csharp
public virtual async Task DeleteAsync({idType} id, string? expectedStamp = null, CancellationToken cancellationToken = default)
```

- [ ] **Step 4: Generate stamped DeleteAsync body**

Replace the generated `DeleteAsync` body with the concurrency-aware version. The code at ~lines 662-671 currently reads:

```csharp
builder.AppendLine($"        public virtual async Task DeleteAsync({idType} id, CancellationToken cancellationToken = default)");
builder.AppendLine("        {");
builder.AppendLine("            var entity = await _repository.GetByIdAsync(id, cancellationToken);");
builder.AppendLine("            if (entity == null)");
builder.AppendLine($"                throw new EntityNotFoundException(typeof({entityName}), id);");
builder.AppendLine();
builder.AppendLine($"            await OnDeletingAsync(entity, cancellationToken);");
builder.AppendLine("            await _repository.DeleteAsync(entity, cancellationToken);");
builder.AppendLine($"            await OnDeletedAsync(entity, cancellationToken);");
builder.AppendLine("        }");
```

Replace with:

```csharp
builder.AppendLine($"        public virtual async Task DeleteAsync({idType} id, string? expectedStamp = null, CancellationToken cancellationToken = default)");
builder.AppendLine("        {");
builder.AppendLine($"            if (typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof({entityName})))");
builder.AppendLine("            {");
builder.AppendLine("                if (string.IsNullOrEmpty(expectedStamp))");
builder.AppendLine("                {");
builder.AppendLine($"                    throw new CrestPreconditionRequiredException(typeof({entityName}).Name, id);");
builder.AppendLine("                }");
builder.AppendLine("                await _repository.DeleteAsync(id, expectedStamp!, cancellationToken);");
builder.AppendLine("                return;");
builder.AppendLine("            }");
builder.AppendLine();
builder.AppendLine("            var entity = await _repository.GetByIdAsync(id, cancellationToken);");
builder.AppendLine("            if (entity == null)");
builder.AppendLine($"                throw new EntityNotFoundException(typeof({entityName}), id);");
builder.AppendLine();
builder.AppendLine($"            await OnDeletingAsync(entity, cancellationToken);");
builder.AppendLine("            await _repository.DeleteAsync(entity, cancellationToken);");
builder.AppendLine($"            await OnDeletedAsync(entity, cancellationToken);");
builder.AppendLine("        }");
```

Add the appropriate using directives to the generated file header for `IHasConcurrencyStamp` and `CrestPreconditionRequiredException`.

- [ ] **Step 5: Add [UnitOfWorkMo] to generated UpdateAsync and DeleteAsync**

In the generated CRUD service class, add `[UnitOfWorkMo]` attribute before both `UpdateAsync` and the new `DeleteAsync` method signatures.

- [ ] **Step 6: Build and test**

```bash
dotnet build framework/tools/CrestCreates.CodeGenerator
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs
git commit -m "feat: generate concurrency-aware UpdateDto, DeleteAsync with If-Match enforcement, [UnitOfWorkMo]"
```

---

### Task 12: LibraryDbContext — call ConfigureConcurrencyStamp

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.EntityFrameworkCore/LibraryDbContext.cs`

- [ ] **Step 1: Add call in OnModelCreating**

Edit `samples/LibraryManagement/LibraryManagement.EntityFrameworkCore/LibraryDbContext.cs`. Add after existing usings:

```csharp
using CrestCreates.OrmProviders.EFCore.Extensions;
```

Add at the end of `OnModelCreating` (before the closing brace):

```csharp
            modelBuilder.ConfigureConcurrencyStamp();
```

- [ ] **Step 2: Build**

```bash
dotnet build samples/LibraryManagement/LibraryManagement.EntityFrameworkCore
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add samples/LibraryManagement/LibraryManagement.EntityFrameworkCore/LibraryDbContext.cs
git commit -m "fix: call ConfigureConcurrencyStamp in LibraryDbContext.OnModelCreating"
```

---

### Task 13: Integration Tests

**Files:**
- Create: `framework/test/CrestCreates.IntegrationTests/ConcurrencyIntegrationTests.cs`

- [ ] **Step 1: Write integration tests**

Create `framework/test/CrestCreates.IntegrationTests/ConcurrencyIntegrationTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class ConcurrencyIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ConcurrencyIntegrationTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Update_WithStaleStamp_Returns409()
    {
        // Create entity
        var createDto = new { Title = "Test Book", Author = "Author" };
        var createResponse = await _client.PostAsJsonAsync("/api/books", createDto);
        createResponse.EnsureSuccessStatusCode();
        var getDto = await createResponse.Content.ReadFromJsonAsync<BookDto>();

        // Update with correct stamp
        var updateDto = new { Id = getDto!.Id, Title = "Updated", Author = "Author", ConcurrencyStamp = getDto.ConcurrencyStamp };
        var updateResponse = await _client.PutAsJsonAsync($"/api/books/{getDto.Id}", updateDto);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Update again with OLD (now stale) stamp
        updateDto = new { Id = getDto.Id, Title = "Stale Update", Author = "Author", ConcurrencyStamp = getDto.ConcurrencyStamp };
        updateResponse = await _client.PutAsJsonAsync($"/api/books/{getDto.Id}", updateDto);
        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_WithStaleStamp_Returns409()
    {
        var createDto = new { Title = "Delete Book", Author = "Author" };
        var createResponse = await _client.PostAsJsonAsync("/api/books", createDto);
        createResponse.EnsureSuccessStatusCode();
        var getDto = await createResponse.Content.ReadFromJsonAsync<BookDto>();

        // Delete with stale stamp
        _client.DefaultRequestHeaders.Add("If-Match", Guid.NewGuid().ToString());
        var deleteResponse = await _client.DeleteAsync($"/api/books/{getDto!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutIfMatch_OnConcurrentEntity_Returns428()
    {
        var createDto = new { Title = "NoStamp Book", Author = "Author" };
        var createResponse = await _client.PostAsJsonAsync("/api/books", createDto);
        createResponse.EnsureSuccessStatusCode();
        var getDto = await createResponse.Content.ReadFromJsonAsync<BookDto>();

        // Delete WITHOUT If-Match header
        var deleteResponse = await _client.DeleteAsync($"/api/books/{getDto!.Id}");
        Assert.Equal((HttpStatusCode)428, deleteResponse.StatusCode);
    }

    private class BookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string ConcurrencyStamp { get; set; } = "";
    }
}
```

- [ ] **Step 2: Run integration tests**

```bash
dotnet test framework/test/CrestCreates.IntegrationTests --filter "FullyQualifiedName~ConcurrencyIntegrationTests"
```

Expected: depends on test infrastructure setup. May need adjustment.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.IntegrationTests/ConcurrencyIntegrationTests.cs
git commit -m "test: add integration tests for concurrency 409 and 428 scenarios"
```

---

### Task 14: Safety Net Test — All DbContexts have IsConcurrencyToken

**Files:**
- Create: `framework/test/CrestCreates.OrmProviders.Tests/ConcurrencyTokenConfigurationTests.cs`

- [ ] **Step 1: Write test**

Create `framework/test/CrestCreates.OrmProviders.Tests/ConcurrencyTokenConfigurationTests.cs`:

```csharp
using System;
using System.Linq;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class ConcurrencyTokenConfigurationTests
{
    [Fact]
    public void CrestCreatesDbContext_AllIHasConcurrencyStampEntities_HaveConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"token-check-{Guid.NewGuid():N}")
            .Options;

        using var context = new CrestCreatesDbContext(options);
        var model = context.Model;

        var hasConcurrencyEntities = model.GetEntityTypes()
            .Where(et => typeof(IHasConcurrencyStamp).IsAssignableFrom(et.ClrType))
            .ToList();

        foreach (var entityType in hasConcurrencyEntities)
        {
            var stampProp = entityType.FindProperty(nameof(IHasConcurrencyStamp.ConcurrencyStamp));
            Assert.NotNull(stampProp);
            Assert.True(stampProp.IsConcurrencyToken,
                $"{entityType.ClrType.Name}.ConcurrencyStamp must be IsConcurrencyToken. "
                + "Did you forget to call modelBuilder.ConfigureConcurrencyStamp()?");
        }
    }

    [Fact]
    public void ModelBuilderExtensions_ConfigureConcurrencyStamp_Works()
    {
        var options = new DbContextOptionsBuilder<DbContext>()
            .UseInMemoryDatabase($"ext-check-{Guid.NewGuid():N}")
            .Options;

        using var context = new TestDbContext(options);
        var model = context.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        Assert.NotNull(entityType);
        var stampProp = entityType!.FindProperty(nameof(IHasConcurrencyStamp.ConcurrencyStamp));
        Assert.NotNull(stampProp);
        Assert.True(stampProp.IsConcurrencyToken);
    }

    private class TestEntity : AuditedEntity<Guid> { public string Name { get; set; } = ""; }

    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        public DbSet<TestEntity> TestEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(100);
            });
            modelBuilder.ConfigureConcurrencyStamp();
        }
    }
}
```

- [ ] **Step 2: Run test**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~ConcurrencyTokenConfigurationTests"
```

Expected: PASS (both).

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.OrmProviders.Tests/ConcurrencyTokenConfigurationTests.cs
git commit -m "test: add safety net test — verify IsConcurrencyToken configured on all IHasConcurrencyStamp entities"
```

---

### Task 15: Build Full Solution & Run All Tests

- [ ] **Step 1: Full build**

```bash
dotnet build
```

Expected: PASS.

- [ ] **Step 2: Run all tests**

```bash
dotnet test
```

Expected: all tests PASS.

- [ ] **Step 3: Fix any failures; commit fixes**
