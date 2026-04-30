# Concurrency Control Design

**Date**: 2026-04-30
**Scope**: 乐观并发控制主链（单一方案，不要多套并存）

## Decision Summary

| Decision | Choice |
|----------|--------|
| Strategy | Optimistic concurrency (optimistic lock) |
| Field type | `string ConcurrencyStamp` (Guid-based) |
| Entity scope | `AuditedEntity<TId>` and `AuditedAggregateRoot<TId>` (and all subclasses) |
| DTO exposure | UpdateDto includes ConcurrencyStamp; CreateDto excludes it |
| ORM behavior | All 3 ORMs check expected stamp in WHERE clause; 0 rows → throw |
| Error code | HTTP 409, `CONCURRENCY_CONFLICT` |
| Operations | **Update AND Delete** both check concurrency stamp |

## 1. Interface Layer

### 1.1 New: `IHasConcurrencyStamp`

**File**: `framework/src/CrestCreates.Domain.Shared/Entities/Auditing/IHasConcurrencyStamp.cs`

```csharp
namespace CrestCreates.Domain.Shared.Entities.Auditing;

public interface IHasConcurrencyStamp
{
    string ConcurrencyStamp { get; set; }
}
```

- Placed in `Domain.Shared` so all layers can reference it.
- `string` type for maximum cross-ORM compatibility.

### 1.2 Modified: `AuditedEntity<TId>` and `AuditedAggregateRoot<TId>`

Both add `IHasConcurrencyStamp` and the property:

```csharp
public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
```

- Default value is a fresh GUID — new entities get a valid stamp on construction.
- `FullyAuditedEntity`, `FullyAuditedAggregateRoot`, and any custom subclass inherit this automatically.
- `Entity<TId>` and `AggregateRoot<TId>` are **not** modified — pure domain entities without auditing are not forced to carry concurrency stamps.

### 1.3 Entity Scope Inventory

Entities that **ARE covered** (inherit from `AuditedEntity` / `AuditedAggregateRoot` / `FullyAuditedEntity` / `FullyAuditedAggregateRoot`):

- `Tenant` (AuditedAggregateRoot)
- `SettingValue` (AuditedAggregateRoot)
- `FeatureValue` (AuditedAggregateRoot)
- All user-defined entities using `[Entity]` with `FullyAuditedEntity<TId>` base (e.g., `Book`)

Entities that are **NOT covered** (inherit from `Entity<TId>` or `MustHaveTenantOrganizationEntity<TId>` directly):

- `Permission`, `PermissionGrant` (Entity<Guid>)
- `Role`, `User`, `Organization` (MustHaveTenantOrganizationEntity<Guid> — has audit fields inline but does not inherit from AuditedEntity)
- `RefreshToken`, `AuditLog`, `IdentitySecurityLog`, `TenantConnectionString`, `TenantDomainMapping`, `DataPermission`, `RolePermission`

Entities not covered can add `IHasConcurrencyStamp` manually if concurrency protection is needed. This is intentional: the default scope is "audited entities that represent user-modifiable business data." Internal infrastructure entities (logs, tokens, mappings) typically don't need optimistic concurrency.

## 2. Stamp Lifecycle

The complete flow from read to write:

```
Client reads GetDto (ConcurrencyStamp = "abc-123")
    ↓
Client sends UpdateDto { Id=5, Title="New", ConcurrencyStamp= "abc-123" }
    ↓
AppService: Repository.GetAsync(5) → entity (ConcurrencyStamp = "abc-123" from DB)
AppService: MapToEntity(input, entity) → entity.ConcurrencyStamp = "abc-123" (expected stamp from client)
    ↓
Repository.UpdateAsync(entity):
  oldStamp = entity.ConcurrencyStamp                    // "abc-123"  ← expected stamp
  newStamp = Guid.NewGuid().ToString()                   // "def-456"  ← new stamp
  entity.ConcurrencyStamp = newStamp                     // set new value on entity
  UPDATE ... SET ConcurrencyStamp = 'def-456'           // write new stamp
         WHERE Id = 5 AND ConcurrencyStamp = 'abc-123'  // check expected stamp
    ↓
  rows == 1 → success, entity returned with new stamp "def-456"
  rows == 0 → throw CrestConcurrencyException (someone else updated between read and write)
```

**Rules**:
- `UpdateDto.ConcurrencyStamp` is the **expected stamp** (what the client read).
- `MapToEntity` / `ApplyTo` copies it to `entity.ConcurrencyStamp` verbatim.
- Repository reads `entity.ConcurrencyStamp` as the **expected stamp** for the WHERE clause.
- Repository **always generates a new stamp** before persisting. Every successful update changes the stamp.
- After a successful update, the returned DTO contains the new stamp — client must use it for the next update.

### Delete flow (same pattern):

```
Client sends Delete request with ConcurrencyStamp = "abc-123"
    ↓
Repository.DeleteAsync(id, expectedStamp):
  DELETE FROM X WHERE Id = 5 AND ConcurrencyStamp = 'abc-123'
    ↓
  rows == 1 → success
  rows == 0 → throw CrestConcurrencyException
```

- `DeleteAsync(TKey id)` does NOT check concurrency (backward compat).
- `DeleteAsync(TKey id, string expectedStamp)` is the new overload that checks concurrency.
- The generated CRUD service calls the overload with stamp when the entity has `IHasConcurrencyStamp`.

## 3. Repository Layer

### 3.1 UpdateAsync

Core rule: if `entity is IHasConcurrencyStamp stamp`:

1. Read `oldStamp = stamp.ConcurrencyStamp` (expected stamp from client).
2. Generate `newStamp = Guid.NewGuid().ToString()`.
3. Set `stamp.ConcurrencyStamp = newStamp`.
4. Execute UPDATE with `WHERE Id = @id AND ConcurrencyStamp = @oldStamp`.
5. If affected rows == 0 → throw `CrestConcurrencyException`.

If entity does NOT implement `IHasConcurrencyStamp` → existing code path (no concurrency check).

#### EF Core

```csharp
if (entity is IHasConcurrencyStamp stamp)
{
    var entry = _dbContext.Set<TEntity>().Update(entity);
    entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue = oldStamp;
    entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).IsModified = true;
}
else
{
    _dbContext.Set<TEntity>().Update(entity);
}

await _dbContext.SaveChangesAsync(cancellationToken);
// DbUpdateConcurrencyException → throw new CrestConcurrencyException(...)
```

Requires `ConcurrencyStamp` to be configured as a concurrency token (see Section 4).

#### FreeSql

```csharp
if (entity is IHasConcurrencyStamp stamp)
{
    var rows = await _orm.Update<TEntity>()
        .SetSource(entity)
        .Where($"Id = {{0}} AND ConcurrencyStamp = {{1}}", entity.Id, oldStamp)
        .ExecuteAffrowsAsync(cancellationToken);

    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
}
else
{
    await _orm.Update<TEntity>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);
}
```

Use string-format `Where` (not interface-cast expression) to guarantee ORM translatability across FreeSql versions.

#### SqlSugar

```csharp
if (entity is IHasConcurrencyStamp stamp)
{
    var rows = await _sqlSugarClient.Updateable(entity)
        .Where($"Id = @Id AND ConcurrencyStamp = @OldStamp", new { Id = entity.Id, OldStamp = oldStamp })
        .ExecuteCommandAsync();

    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
}
else
{
    await _sqlSugarClient.Updateable(entity).ExecuteCommandAsync();
}
```

Use string-format `Where` to explicitly compare against the old stamp value. `WhereColumns` is not suitable — it copies column values from the entity, but the entity already has the new stamp value at execution time.

### 3.2 DeleteAsync (with concurrency stamp)

Add a new overload to `CrestRepositoryBase<TEntity, TKey>`:

```csharp
public abstract Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default);
```

Implementation pattern (same across ORMs):

```csharp
// EF Core
var rows = await _dbContext.Set<TEntity>()
    .Where(e => e.Id.Equals(id) && ((IHasConcurrencyStamp)e).ConcurrencyStamp == expectedStamp)
    .ExecuteDeleteAsync(cancellationToken);

if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);

// FreeSql
var rows = await _orm.Delete<TEntity>()
    .Where($"Id = {{0}} AND ConcurrencyStamp = {{1}}", id, expectedStamp)
    .ExecuteAffrowsAsync(cancellationToken);

if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);

// SqlSugar
var rows = await _sqlSugarClient.Deleteable<TEntity>()
    .Where($"Id = @Id AND ConcurrencyStamp = @Stamp", new { Id = id, Stamp = expectedStamp })
    .ExecuteCommandAsync();

if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
```

Existing `DeleteAsync(TKey id)` and `DeleteAsync(TEntity entity)` remain unchanged for backward compatibility.

### 3.3 UpdateRangeAsync (Batch Update)

Same concurrency logic — each entity that implements `IHasConcurrencyStamp` gets a new stamp and WHERE check on its old stamp.

**Transaction requirement**: UpdateRangeAsync with concurrency MUST execute within a transaction. If no ambient transaction exists, wrap in one. If any entity conflicts, the transaction is rolled back so no partial updates persist.

```csharp
// Pseudocode
using var tx = BeginTransactionIfNone();
try
{
    foreach (var entity in entities)
    {
        if (entity is IHasConcurrencyStamp stamp)
        {
            var oldStamp = stamp.ConcurrencyStamp;
            stamp.ConcurrencyStamp = Guid.NewGuid().ToString();
            // execute update with WHERE ConcurrencyStamp = oldStamp
            // if rows == 0 → throw, tx rolls back
        }
        else
        {
            // normal update
        }
    }
    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}
```

## 4. EF Core Model Configuration

### New: `ConfigureConcurrencyStamp` extension method

**File**: `framework/src/CrestCreates.OrmProviders.EFCore/Extensions/ModelBuilderExtensions.cs` (or similar)

```csharp
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
```

All framework DbContext `OnModelCreating` methods must call `modelBuilder.ConfigureConcurrencyStamp()`. Sample project DbContexts do the same if they override `OnModelCreating`.

Without this configuration, EF Core does not include `ConcurrencyStamp` in the UPDATE WHERE clause and does not throw `DbUpdateConcurrencyException` — it would silently overwrite.

## 5. Exception & Propagation

### 5.1 New: `CrestConcurrencyException`

**File**: `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs`

```csharp
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

- Base class is `Exception` for now. After the global-exception-handling epic, switch to a unified base class.

### 5.2 Application Service: Must Re-throw (not wrap)

`CrestAppServiceBase.UpdateAsync` (line 243) has a catch-all `catch (Exception ex)` that wraps everything into a generic `Exception`. This would swallow `CrestConcurrencyException` and prevent the middleware from recognizing it.

**Fix**: Add `CrestConcurrencyException` to the re-throw list alongside `KeyNotFoundException`:

```csharp
catch (KeyNotFoundException)
{
    throw;
}
catch (CrestConcurrencyException)  // <-- ADD
{
    throw;
}
catch (DbException ex)
{
    throw new Exception($"...", ex);
}
catch (Exception ex)
{
    throw new Exception($"...", ex);
}
```

Same fix for `DeleteAsync` if it gains concurrency support.

### 5.3 Exception Middleware: Add 409 Mapping

**File**: `framework/src/CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs`

Add a case in the switch statement:

```csharp
case CrestConcurrencyException concurrencyEx:
    context.Response.StatusCode = (int)HttpStatusCode.Conflict;  // 409
    errorResponse.Code = (int)HttpStatusCode.Conflict;
    errorResponse.Message = "数据已被其他用户修改，请刷新后重试";
    errorResponse.Details = concurrencyEx.Message;
    logger.LogWarning(concurrencyEx, "Concurrency conflict for request {TraceId}", context.TraceIdentifier);
    break;
```

## 6. Code Generator Changes

Two generators need changes:

### 6.1 EntitySourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`

| Method | Change |
|--------|--------|
| `GenerateCreateEntityDto` (line 1082) | Add `"ConcurrencyStamp"` to the exclusion list: `...p.Name != "ConcurrencyStamp"` |
| `GenerateUpdateEntityDto` (line 1123) | No change — already does NOT exclude ConcurrencyStamp (correct) |
| `GenerateEntityDto` (line 1045) | No change — already includes all properties (correct) |
| `GenerateMappingExtensions` → `UpdateXxxDto.ApplyTo()` (line 1246) | No change — ConcurrencyStamp passes the writable non-audit filter (correct) |

The `ApplyTo` for UpdateDto already maps `ConcurrencyStamp` from DTO to entity automatically (it's not in the exclusion list at line 1176-1181). This correctly provides the expected stamp to the repository.

### 6.2 CrudServiceSourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

| Method | Change |
|--------|--------|
| `GenerateCreateEntityDto` (line 263) | Already excludes ConcurrencyStamp — no change needed |
| `GenerateUpdateEntityDto` (line 311) | Remove `"ConcurrencyStamp"` from the exclusion list |
| Generated `UpdateAsync` (line ~580) | Map `input.ConcurrencyStamp` to entity before repository call |
| Generated `DeleteAsync` | When entity has `IHasConcurrencyStamp`, call `DeleteAsync(id, input.ConcurrencyStamp)` instead of `DeleteAsync(id)` |

The generated UpdateAsync maps the expected stamp from DTO to entity:

```csharp
// Inside generated UpdateAsync method body:
var entity = await Repository.GetAsync(id, cancellationToken);
if (entity == null) throw new KeyNotFoundException($"...");

entity = MapToEntity(input, entity);  // This sets entity.ConcurrencyStamp = input.ConcurrencyStamp (expected stamp)
// ... audit properties ...
var result = await Repository.UpdateAsync(entity, cancellationToken);  // Repository generates new stamp
```

## 7. Testing

### Test scenarios (EF Core — full coverage)

| # | Test | Description |
|---|------|-------------|
| 1 | `Update_WithCorrectStamp_ShouldSucceed` | Matching stamp → success, returned DTO has new stamp ≠ old stamp |
| 2 | `Update_WithStaleStamp_ShouldThrowConcurrencyException` | Old stamp → `CrestConcurrencyException` |
| 3 | `Update_WithStaleStamp_ShouldReturn409` | API-level: stale stamp → HTTP 409 JSON response |
| 4 | `ConcurrentUpdate_TwoRequests_OneSucceedsOneFails` | Two parallel tasks → one 200, one 409 |
| 5 | `Delete_WithCorrectStamp_ShouldSucceed` | Matching stamp → delete succeeds |
| 6 | `Delete_WithStaleStamp_ShouldThrowConcurrencyException` | Old stamp → `CrestConcurrencyException` |
| 7 | `Entity_WithoutConcurrency_ShouldStillWork` | Entity w/o `IHasConcurrencyStamp` → old behavior preserved |
| 8 | `NewEntity_GetsConcurrencyStampOnConstruction` | New AuditedEntity → ConcurrencyStamp is a valid GUID string |

### Cross-ORM coverage

- EF Core: full coverage (all 8 scenarios).
- FreeSql: scenario 2 and 6 (update/delete stale stamp → throw).
- SqlSugar: scenario 2 and 6 (update/delete stale stamp → throw).

### Test entity

Use existing `Book` entity (`FullyAuditedEntity<Guid>`) to test on the real production entity path.

## 8. Files Changed

| File | Action |
|------|--------|
| `CrestCreates.Domain.Shared/.../IHasConcurrencyStamp.cs` | New |
| `CrestCreates.Domain/.../AuditedEntity.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `CrestCreates.Domain/.../AuditedAggregateRoot.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `CrestCreates.Domain/.../CrestConcurrencyException.cs` | New |
| `CrestCreates.Domain/.../CrestRepositoryBase.cs` | Add `DeleteAsync(TKey id, string expectedStamp, ...)` abstract method |
| `CrestCreates.OrmProviders.EFCore/.../ModelBuilderExtensions.cs` (or similar) | New: `ConfigureConcurrencyStamp()` extension |
| `CrestCreates.OrmProviders.EFCore/.../EfCoreRepository.cs` | Concurrency in `UpdateAsync`, `DeleteAsync`, `UpdateRangeAsync` |
| `CrestCreates.OrmProviders.EFCore/.../EfCoreRepositoryBase.cs` | Concurrency in `UpdateAsync`, `DeleteAsync`, `UpdateRangeAsync` |
| `CrestCreates.OrmProviders.FreeSqlProvider/.../FreeSqlRepositoryBase.cs` | Concurrency in `UpdateAsync`, `DeleteAsync`, `UpdateRangeAsync` |
| `CrestCreates.OrmProviders.SqlSugar/.../SqlSugarRepository.cs` | Concurrency in `UpdateAsync`, `DeleteAsync`, `UpdateRangeAsync` |
| `CrestCreates.Application/.../CrestAppServiceBase.cs` | Re-throw `CrestConcurrencyException` in `UpdateAsync` / `DeleteAsync` |
| `CrestCreates.Web/.../ExceptionHandlingMiddleware.cs` | Add `CrestConcurrencyException` → 409 case |
| `CrestCreates.CodeGenerator/.../EntitySourceGenerator.cs` | Exclude `ConcurrencyStamp` from `CreateXxxDto` |
| `CrestCreates.CodeGenerator/.../CrudServiceSourceGenerator.cs` | Include `ConcurrencyStamp` in `UpdateXxxDto`; concurrency-aware update/delete generation |
| All framework DbContext `OnModelCreating` | Call `modelBuilder.ConfigureConcurrencyStamp()` |
| Test projects | 8 concurrency conflict tests |
