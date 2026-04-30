# Concurrency Control Design

**Date**: 2026-04-30
**Scope**: 乐观并发控制主链（单一方案，不要多套并存）

## Decision Summary

| Decision | Choice |
|----------|--------|
| Strategy | Optimistic concurrency |
| Field type | `string ConcurrencyStamp` (Guid) |
| Entity scope | `AuditedEntity<TId>` and `AuditedAggregateRoot<TId>` (and all subclasses) |
| DTO exposure | GetDto includes stamp; UpdateDto includes stamp; CreateDto excludes stamp |
| Update | Client sends expected stamp via UpdateDto; repository WHERE-checks it, generates new stamp |
| Delete | Client sends expected stamp via `If-Match` header; **required** for `IHasConcurrencyStamp` entities — no silent fallback |
| Delete for non-concurrency entities | `If-Match` header is ignored; plain delete by id |
| Error code | HTTP 409 for stamp mismatch; 428 for missing `If-Match` on concurrent entity |
| ORM behavior | All 3 ORMs: WHERE includes expected stamp; 0 rows → throw `CrestConcurrencyException` |

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

`string` type for maximum cross-ORM compatibility. Placed in `Domain.Shared` — all layers can reference.

### 1.2 Entity Changes

`AuditedEntity<TId>` and `AuditedAggregateRoot<TId>` add `IHasConcurrencyStamp` and:

```csharp
public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
```

- Default is a fresh GUID — new entities get a valid stamp on construction.
- `FullyAuditedEntity`, `FullyAuditedAggregateRoot`, and custom subclasses inherit automatically.
- `Entity<TId>` and `AggregateRoot<TId>` are **not** modified.

### 1.3 Entity Scope Inventory

**Covered**: `Tenant`, `SettingValue`, `FeatureValue`, all user entities with `FullyAuditedEntity<TId>` base.

**Not covered**: `Permission`, `PermissionGrant`, `Role`, `User`, `Organization`, `RefreshToken`, `AuditLog`, `IdentitySecurityLog`, `TenantConnectionString`, `TenantDomainMapping`, `DataPermission`, `RolePermission`, `UserRole`.

Infrastructure/internal entities don't need optimistic concurrency. Add `IHasConcurrencyStamp` manually if later needed.

## 2. Repository Interfaces — Main Chain vs Legacy

The framework has **three** repository interface families. This design touches only the main chain:

| Interface | Location | Role | Concurrency changes |
|-----------|----------|------|---------------------|
| `ICrestRepositoryBase<TEntity, TKey>` | Domain.Repositories | **Main chain** — used by `CrestAppServiceBase` and generated CRUD services | Add `DeleteAsync(TKey id, string expectedStamp, ...)` |
| `CrestRepositoryBase<TEntity, TKey>` | Domain.Repositories | Abstract base for all ORM repos | Add abstract `DeleteAsync(TKey id, string expectedStamp, ...)` |
| `IRepository<TEntity, TId>` | Domain.Repositories | Legacy — used by `CrudServiceBase` | **No changes** |
| `IRepository<TEntity>` / `IRepository<TEntity, TKey>` | OrmProviders.Abstract.Abstractions | ORM-level abstractions | **No changes** |

The generated CRUD service and `CrestAppServiceBase` use `ICrestRepositoryBase`. `CrudServiceBase` uses `Domain.Repositories.IRepository` — it is legacy and gets no concurrency support.

## 3. Stamp Lifecycle

### 3.1 Update Flow

```
Client reads GetDto → ConcurrencyStamp = "abc-123"
Client sends PUT /x/5 with UpdateDto { ..., ConcurrencyStamp = "abc-123" }
  ↓
AppService: Repository.GetAsync(5) → entity (DB stamp is whatever it is)
AppService: MapToEntity(input, entity) → entity.ConcurrencyStamp = "abc-123"  ← expected
  ↓
Repository.UpdateAsync(entity):
  oldStamp = entity.ConcurrencyStamp               // "abc-123"
  newStamp = Guid.NewGuid().ToString()              // "ghi-789"
  entity.ConcurrencyStamp = newStamp
  UPDATE SET [all], ConcurrencyStamp='ghi-789' WHERE Id=5 AND ConcurrencyStamp='abc-123'
  ↓
  rows == 1 → success, returned entity.ConcurrencyStamp = "ghi-789"
  rows == 0 → throw CrestConcurrencyException
```

Concurrency safety comes from **the `WHERE ConcurrencyStamp = expectedStamp` clause**, not from transaction isolation. Every successful update produces a new stamp.

### 3.2 Delete Flow (concurrent entity)

```
Client reads GetDto → ConcurrencyStamp = "abc-123"
Client sends DELETE /x/5 with If-Match: "abc-123"
  ↓
Controller reads [FromHeader] If-Match → expectedStamp = "abc-123"
Controller calls Service.DeleteAsync(5, expectedStamp: "abc-123")
  ↓
Generated service: expectedStamp != null → Repository.DeleteAsync(5, "abc-123")
  ↓
  DELETE FROM X WHERE Id=5 AND ConcurrencyStamp='abc-123'
  ↓
  rows == 1 → success
  rows == 0 → throw CrestConcurrencyException
```

**Single atomic DELETE** — no pre-read, no TOCTOU race.

### 3.3 Delete: If-Match Required for Concurrent Entities

For entities implementing `IHasConcurrencyStamp`, the generated CRUD service's `DeleteAsync` **requires** the `If-Match` header. Being optional at the interface level (for backward compatibility) does NOT mean silently falling back.

```csharp
// Generated CRUD service DeleteAsync — NO silent fallback:
public virtual async Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken ct = default)
{
    if (typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(Book)))
    {
        if (string.IsNullOrEmpty(expectedStamp))
        {
            throw new CrestPreconditionRequiredException(typeof(Book).Name, id);
        }
        await _repository.DeleteAsync(id, expectedStamp!, ct);
        return;
    }

    // Non-concurrency entity: normal delete by id
    await _repository.DeleteAsync(id, ct);
}
```

Missing `If-Match` → `CrestPreconditionRequiredException` → middleware maps to **428 Precondition Required**.

### 3.4 Server-Side / Internal Delete (non-CRUD)

Internal code that needs to delete entities without a client-provided stamp (e.g., cleanup jobs, admin tools) uses:

- `Repository.DeleteAsync(id)` — plain delete by id, zero concurrency check. Caller accepts the risk.
- `Repository.DeleteAsync(entity)` — uses the entity's DB-read stamp. Has a TOCTOU race (read and delete are separate calls), acceptable for internal callers within a short-lived scope.

These are **not exposed through CRUD controllers**. The strict path (If-Match) is the only path for client-facing CRUD operations on concurrent entities.

## 4. API Contract Changes

### 4.1 `ICrudAppService` — optional stamp parameter

**File**: `framework/src/CrestCreates.Application.Contracts/Interfaces/ICrudAppService.cs`

```csharp
// OLD
Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);

// NEW
Task DeleteAsync(TKey id, string? expectedStamp = null, CancellationToken cancellationToken = default);
```

Optional at the interface level for backward compatibility. The generated implementation enforces it for `IHasConcurrencyStamp` entities (see §3.3).

### 4.2 Controller — `If-Match` header

**File**: `framework/src/CrestCreates.AspNetCore/Controllers/CrudControllerBase.cs`

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

## 5. Repository Layer

### 5.1 New: `DeleteAsync(TKey id, string expectedStamp)`

**`ICrestRepositoryBase`** and **`CrestRepositoryBase`** get:

```csharp
Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default);
```

If the entity type does NOT implement `IHasConcurrencyStamp`, the implementation ignores the stamp and does a plain `DELETE BY Id`. The stamped overload is only semantically meaningful for concurrent entities, but being unconditional avoids a runtime type-check surprise.

#### EF Core

```csharp
var rows = await _dbContext.Set<TEntity>()
    .Where(e => e.Id.Equals(id)
        && EF.Property<string>(e, "ConcurrencyStamp") == expectedStamp)
    .ExecuteDeleteAsync(cancellationToken);
if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
```

`EF.Property<string>` avoids interface-cast expression translation risk. `ExecuteDeleteAsync` is a single atomic SQL statement — no pre-read, no `SaveChanges`.

#### FreeSql

```csharp
var rows = await _orm.Delete<TEntity>()
    .Where("Id = {0} AND ConcurrencyStamp = {1}", id, expectedStamp)
    .ExecuteAffrowsAsync(cancellationToken);
if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
```

#### SqlSugar

```csharp
var rows = await _sqlSugarClient.Deleteable<TEntity>()
    .Where("Id = @Id AND ConcurrencyStamp = @Stamp",
           new { Id = id, Stamp = expectedStamp })
    .ExecuteCommandAsync();
if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
```

### 5.2 UpdateAsync

Same as before: read old stamp from entity, generate new stamp, WHERE-check old stamp.

### 5.3 `DeleteAsync(TEntity entity)` — entity-based, no interface change

Existing overload gains concurrency internally: if entity implements `IHasConcurrencyStamp`, use `entity.ConcurrencyStamp` in WHERE. Has a TOCTOU race (service pre-reads entity, repository deletes separately). Acceptable for internal/server-side callers, not for client CRUD.

### 5.4 UpdateRangeAsync — throws for concurrent entities

```csharp
public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, ...)
{
    var entityList = entities.ToList();
    if (entityList.Any(e => e is IHasConcurrencyStamp))
    {
        throw new NotSupportedException(
            "UpdateRangeAsync does not support entities with IHasConcurrencyStamp. "
            + "Update concurrent entities individually via UpdateAsync.");
    }
    // existing batch update logic...
}
```

All three ORM implementations follow this pattern. A test verifies the `NotSupportedException` is thrown when any entity in the range has `IHasConcurrencyStamp`.

## 6. EF Core Model Configuration

### 6.1 `ConfigureConcurrencyStamp` extension

**File**: `framework/src/CrestCreates.OrmProviders.EFCore/Extensions/ModelBuilderExtensions.cs`

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

Without `IsConcurrencyToken()`, EF Core does not include `ConcurrencyStamp` in the UPDATE WHERE clause and does not throw `DbUpdateConcurrencyException` — silent overwrite.

### 6.2 Where it's called

1. `CrestCreatesDbContext.OnModelCreating` calls `modelBuilder.ConfigureConcurrencyStamp()` at the end.
2. Sample DbContexts inheriting from `CrestCreatesDbContext` get it via `base.OnModelCreating()`.
3. Standalone DbContexts (e.g., `LibraryDbContext`) must call it manually.

### 6.3 Safety net test

A test enumerates **all known DbContext types** in the solution (framework + samples), builds each model, and asserts: for every entity implementing `IHasConcurrencyStamp`, the `ConcurrencyStamp` property has `IsConcurrencyToken == true`. Any DbContext missing the call fails this test.

## 7. Exception & Propagation

### 7.1 `CrestConcurrencyException`

**File**: `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs`

```csharp
public class CrestConcurrencyException : Exception
{
    public string EntityType { get; }
    public object? EntityId { get; }
    // constructor with formatted message...
}
```

### 7.2 `CrestPreconditionRequiredException`

**File**: `framework/src/CrestCreates.Domain/Exceptions/CrestPreconditionRequiredException.cs`

```csharp
public class CrestPreconditionRequiredException : Exception
{
    public string EntityType { get; }
    public object? EntityId { get; }

    public CrestPreconditionRequiredException(string entityType, object? entityId)
        : base($"Precondition required: DELETE on {entityType} (Id={entityId}) "
             + "requires If-Match header with current ConcurrencyStamp.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
```

Thrown by the generated CRUD service when an `IHasConcurrencyStamp` entity is deleted without the `If-Match` header. Mapped to HTTP 428 in the middleware.

### 7.3 Application Service — re-throw white list

`CrestAppServiceBase.UpdateAsync` and `DeleteAsync` catch blocks: add `catch (CrestConcurrencyException) { throw; }` before `catch (Exception)`.

### 7.4 Middleware — 409 and 428 mapping

**File**: `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs`

(`CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs` is **excluded from compilation**.)

```csharp
case CrestConcurrencyException concurrencyEx:
    context.Response.StatusCode = (int)HttpStatusCode.Conflict;   // 409
    errorResponse.Message = "数据已被其他用户修改，请刷新后重试";
    errorResponse.Details = concurrencyEx.Message;
    break;

case CrestPreconditionRequiredException preEx:
    context.Response.StatusCode = 428;                            // Precondition Required
    errorResponse.Message = "请求缺少 If-Match 头，请提供当前 ConcurrencyStamp";
    errorResponse.Details = preEx.Message;
    break;
```

### 7.5 Legacy path: CrudServiceBase

`CrudServiceBase` and `ICrudService` are **legacy**. Mark with:

```csharp
[Obsolete("Use generated CRUD service or CrestAppServiceBase for concurrency support.")]
```

A test verifies `CrudServiceBase.DeleteAsync` does NOT reject stale stamps (proving it has no concurrency awareness). Entities managed through `CrudServiceBase` get zero concurrency protection — this is documented and intentional.

## 8. Code Generator Changes

### 8.1 EntitySourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`

| Method | Change |
|--------|--------|
| `GenerateCreateEntityDto` (~line 1082) | Add `p.Name != "ConcurrencyStamp"` to exclusion |
| `GenerateUpdateEntityDto` (~line 1123) | No change — ConcurrencyStamp is NOT excluded |
| `GenerateEntityDto` (~line 1045) | No change — includes all properties |
| `GenerateMappingExtensions` → `UpdateXxxDto.ApplyTo` (~line 1246) | No change — maps ConcurrencyStamp |

### 8.2 CrudServiceSourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

| Item | Change |
|------|--------|
| `GenerateCreateEntityDto` exclusion list | Already excludes ConcurrencyStamp — no change |
| `GenerateUpdateEntityDto` exclusion list | Remove `"ConcurrencyStamp"` from exclusion |
| Generated `UpdateAsync` body | Map `input.ConcurrencyStamp` to entity before `Repository.UpdateAsync` |
| Generated `DeleteAsync` signature | Add `string? expectedStamp = null` parameter |
| Generated `DeleteAsync` body | For `IHasConcurrencyStamp` entity: require `expectedStamp`, call `Repository.DeleteAsync(id, expectedStamp)`. Missing → throw. For non-concurrency: existing path. |
| Generated `DeleteAsync` for concurrent entity | Add `[UnitOfWorkMo]` to match `CrestAppServiceBase` behavior |

### 8.3 Generated service `[UnitOfWorkMo]`

The generated CRUD service is the official main chain. Its `UpdateAsync` and `DeleteAsync` methods are decorated with `[UnitOfWorkMo]` to ensure audit writes, domain events, and entity changes happen in a single transaction. This is about **side-effect consistency**, not concurrency safety — the concurrency safety comes from the SQL WHERE clause.

## 9. Testing

| # | Test | Description |
|---|------|-------------|
| 1 | `Update_WithCorrectStamp_ShouldSucceed` | Matching stamp → success, new stamp returned |
| 2 | `Update_WithStaleStamp_ShouldThrowConcurrencyException` | Old stamp → `CrestConcurrencyException` |
| 3 | `Update_WithStaleStamp_ShouldReturn409` | API: stale stamp PUT → HTTP 409 |
| 4 | `ConcurrentUpdate_TwoRequests_OneSucceedsOneFails` | Parallel tasks → one 200, one 409 |
| 5 | `Delete_WithCorrectStamp_ShouldSucceed` | Matching `If-Match` → 204 |
| 6 | `Delete_WithStaleStamp_ShouldThrowConcurrencyException` | Stale `If-Match` → `CrestConcurrencyException` |
| 7 | `Delete_WithStaleStamp_ShouldReturn409` | API: stale `If-Match` DELETE → HTTP 409 |
| 8 | `Delete_ConcurrentEntity_WithoutIfMatch_ShouldReturn428` | Missing `If-Match` on concurrent entity → `CrestPreconditionRequiredException` → 428 |
| 9 | `Delete_NonConcurrentEntity_WithoutIfMatch_ShouldSucceed` | Non-concurrency entity: delete succeeds without header |
| 10 | `Entity_WithoutConcurrency_ShouldStillWork` | No `IHasConcurrencyStamp` → old behavior |
| 11 | `NewEntity_GetsConcurrencyStampOnConstruction` | New AuditedEntity → stamp is non-empty GUID |
| 12 | `AllDbContexts_ConcurrencyToken_IsConfigured` | Enumerate all known DbContexts: `IHasConcurrencyStamp` entities have `IsConcurrencyToken == true` |
| 13 | `CrudServiceBase_Delete_HasNoConcurrencyProtection` | Legacy path: stale stamp doesn't prevent delete |
| 14 | `UpdateRangeAsync_WithConcurrentEntity_ThrowsNotSupported` | Batch update with `IHasConcurrencyStamp` → `NotSupportedException` |

### Cross-ORM

- EF Core: full coverage (14 scenarios).
- FreeSql: scenarios 2, 6, 14 (update/delete stale stamp → throw; UpdateRangeAsync → throw).
- SqlSugar: scenarios 2, 6, 14.

## 10. Files Changed

| File | Action |
|------|--------|
| `Domain.Shared/.../IHasConcurrencyStamp.cs` | New |
| `Domain/.../AuditedEntity.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `Domain/.../AuditedAggregateRoot.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `Domain/.../CrestConcurrencyException.cs` | New |
| `Domain/.../CrestPreconditionRequiredException.cs` | New |
| `Domain/.../ICrestRepositoryBase.cs` | Add `DeleteAsync(TKey, string expectedStamp, ...)` |
| `Domain/.../CrestRepositoryBase.cs` | Add abstract `DeleteAsync(TKey, string expectedStamp, ...)` |
| `OrmProviders.EFCore/.../ModelBuilderExtensions.cs` | New: `ConfigureConcurrencyStamp()` |
| `OrmProviders.EFCore/.../CrestCreatesDbContext.cs` | Call `modelBuilder.ConfigureConcurrencyStamp()` |
| `OrmProviders.EFCore/.../EfCoreRepository.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(entity)`, `DeleteAsync(id, stamp)`, `UpdateRangeAsync` |
| `OrmProviders.EFCore/.../EfCoreRepositoryBase.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(entity)`, `DeleteAsync(id, stamp)`, `UpdateRangeAsync` |
| `OrmProviders.FreeSqlProvider/.../FreeSqlRepositoryBase.cs` | Same concurrency methods |
| `OrmProviders.SqlSugar/.../SqlSugarRepository.cs` | Same concurrency methods |
| `Application.Contracts/.../ICrudAppService.cs` | `DeleteAsync` add `string? expectedStamp = null` |
| `Application/.../CrestAppServiceBase.cs` | Re-throw `CrestConcurrencyException` |
| `Application/.../CrudServiceBase.cs` | `[Obsolete]` |
| `Application/.../ICrudService.cs` | `[Obsolete]` |
| `AspNetCore/.../CrudControllerBase.cs` | `DeleteAsync` reads `[FromHeader] If-Match` |
| `AspNetCore/.../ExceptionHandlingMiddleware.cs` | 409 + 428 cases |
| `CodeGenerator/.../EntitySourceGenerator.cs` | Exclude `ConcurrencyStamp` from `CreateXxxDto` |
| `CodeGenerator/.../CrudServiceSourceGenerator.cs` | Include stamp in `UpdateXxxDto`; stamped `DeleteAsync` with enforcement; `[UnitOfWorkMo]` on generated service methods |
| `samples/.../LibraryDbContext.cs` (or any standalone DbContext) | Call `modelBuilder.ConfigureConcurrencyStamp()` |
| Test projects | 14 scenarios |
