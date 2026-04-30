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
| Update stamp | Client sends expected stamp via UpdateDto; repository checks it in WHERE, generates new stamp |
| Delete stamp | Client sends expected stamp via `If-Match` header; repository checks it in WHERE — **one atomic DELETE, no pre-read** |
| ORM behavior | All 3 ORMs: WHERE includes expected stamp; 0 rows → throw `CrestConcurrencyException` |
| Error code | HTTP 409, `CONCURRENCY_CONFLICT` |
| Legacy path | `CrudServiceBase` / `ICrudService` — no concurrency support. Use generated CRUD service or `CrestAppServiceBase`. |

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

### 1.2 Entity Changes

`AuditedEntity<TId>` and `AuditedAggregateRoot<TId>` add `IHasConcurrencyStamp` and:

```csharp
public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
```

- Default is a fresh GUID — new entities get a valid stamp on construction.
- `FullyAuditedEntity`, `FullyAuditedAggregateRoot`, and custom subclasses inherit automatically.
- `Entity<TId>` and `AggregateRoot<TId>` are **not** modified.

### 1.3 Entity Scope Inventory

**Covered** (inherit from `AuditedEntity` / `AuditedAggregateRoot`): `Tenant`, `SettingValue`, `FeatureValue`, all user-defined entities with `FullyAuditedEntity<TId>` base.

**Not covered** (inherit from `Entity<TId>` or `MustHaveTenantOrganizationEntity<TId>` directly): `Permission`, `PermissionGrant`, `Role`, `User`, `Organization`, `RefreshToken`, `AuditLog`, `IdentitySecurityLog`, `TenantConnectionString`, `TenantDomainMapping`, `DataPermission`, `RolePermission`, `UserRole`.

These are infrastructure/internal entities that don't need optimistic concurrency. If needed later, add `IHasConcurrencyStamp` manually.

## 2. Stamp Lifecycle

### 2.1 Update Flow

```
Client reads GetDto → ConcurrencyStamp = "abc-123"
Client sends PUT /x/5 with UpdateDto { ..., ConcurrencyStamp = "abc-123" }
  ↓
AppService: Repository.GetAsync(5) → entity (DB stamp = "def-456")
AppService: MapToEntity(input, entity) → entity.ConcurrencyStamp = "abc-123"  ← expected from client
  ↓
Repository.UpdateAsync(entity):
  oldStamp = entity.ConcurrencyStamp               // "abc-123"  ← expected
  newStamp = Guid.NewGuid().ToString()              // "ghi-789"  ← new
  entity.ConcurrencyStamp = newStamp
  UPDATE SET [all], ConcurrencyStamp='ghi-789' WHERE Id=5 AND ConcurrencyStamp='abc-123'
  ↓
  rows == 1 → success, entity returned with stamp "ghi-789"
  rows == 0 → throw CrestConcurrencyException
```

Every successful update produces a new stamp. The client must use the returned stamp for its next update.

### 2.2 Delete Flow

```
Client reads GetDto → ConcurrencyStamp = "abc-123"
Client sends DELETE /x/5 with If-Match: "abc-123"
  ↓
Controller reads [FromHeader] If-Match → expectedStamp = "abc-123"
Controller calls Service.DeleteAsync(5, expectedStamp: "abc-123")
  ↓
AppService: Repository.DeleteAsync(5, expectedStamp: "abc-123")
  ↓
  DELETE FROM X WHERE Id=5 AND ConcurrencyStamp='abc-123'
  ↓
  rows == 1 → success
  rows == 0 → throw CrestConcurrencyException
```

**Single atomic operation** — no pre-read, no TOCTOU race. If the client sends a stale stamp (because someone else modified the entity), the delete is rejected. If the entity does NOT implement `IHasConcurrencyStamp`, the overload ignores the stamp and does a plain `DELETE BY Id`.

## 3. API Contract Changes

### 3.1 `ICrudAppService` — add optional stamp parameter

**File**: `framework/src/CrestCreates.Application.Contracts/Interfaces/ICrudAppService.cs`

```csharp
// OLD
Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);

// NEW
Task DeleteAsync(TKey id, string? expectedStamp = null, CancellationToken cancellationToken = default);
```

Rationale: if `expectedStamp` is non-null AND the entity implements `IHasConcurrencyStamp`, the service passes it to the repository's stamped delete overload. If null, fall back to entity-based delete (pre-read then delete with server-read stamp — suitable for server-side/internal callers).

All existing implementations must add the parameter. For generated services, the generator emits the new signature automatically.

### 3.2 Controller — `If-Match` header

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

Standard HTTP: `If-Match: "stamp-guid"`. No new route, no body, no query parameter.

## 4. Repository Layer

### 4.1 New: `DeleteAsync(TKey id, string expectedStamp)` overload

**ICrestRepositoryBase** gets:

```csharp
Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default);
```

Concrete repositories implement it:

#### EF Core

```csharp
var rows = await _dbContext.Set<TEntity>()
    .Where(e => e.Id.Equals(id)
        && EF.Property<string>(e, "ConcurrencyStamp") == expectedStamp)
    .ExecuteDeleteAsync(cancellationToken);
if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
```

Uses `EF.Property<string>` to access ConcurrencyStamp generically without an interface cast. `ExecuteDeleteAsync` executes a single atomic `DELETE ... WHERE` — no pre-read, no `SaveChanges`.

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

### 4.2 Entity overload `DeleteAsync(TEntity entity)` — no change

The existing entity-based delete (`DeleteAsync(TEntity entity)`) also gains concurrency: if the entity implements `IHasConcurrencyStamp`, the repository uses `entity.ConcurrencyStamp` as the expected stamp in the WHERE clause.

However, this overload has a **TOCTOU race** between the service-level pre-read (`GetByIdAsync`) and the repository's delete (two separate DB calls). For production CRUD clients, use the stamped `DeleteAsync(id, expectedStamp)` overload instead. The entity overload is retained for server-side/internal callers where the race window is acceptable.

### 4.3 UpdateAsync — unchanged from before

Same as previous revision: read old stamp from entity, generate new stamp, WHERE check with old stamp.

### 4.4 UpdateRangeAsync

First version: **concurrent entities are not supported through UpdateRangeAsync**. Document as a known limitation. If callers need batch updates with concurrency, they must update entities one at a time via `UpdateAsync` within a `[UnitOfWorkMo]`-decorated method.

The UoW decoration on `CrestAppServiceBase.UpdateAsync` wraps each single-entity update in a transaction, ensuring the SELECT (by the service) and UPDATE (by the repository) are atomic. Batch update across multiple entities with mixed concurrency stamps requires caller-controlled transaction boundaries, which the current abstraction doesn't cleanly support across all three ORMs.

## 5. EF Core Model Configuration

### 5.1 `ConfigureConcurrencyStamp` extension

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

Without `IsConcurrencyToken()`, EF Core does not include `ConcurrencyStamp` in the UPDATE WHERE clause and does not throw `DbUpdateConcurrencyException`.

### 5.2 Where it's called

1. `CrestCreatesDbContext.OnModelCreating` calls `modelBuilder.ConfigureConcurrencyStamp()` at the end.
2. Sample DbContexts that inherit from `CrestCreatesDbContext` and call `base.OnModelCreating()` get it automatically.
3. Custom standalone DbContexts (e.g., `LibraryDbContext` which inherits from `DbContext` directly) must call it manually after their own entity configuration.

### 5.3 Safety net test

A test verifies: for every known DbContext in the solution (framework + samples), entities implementing `IHasConcurrencyStamp` have `IsConcurrencyToken == true` on their `ConcurrencyStamp` property in the EF model metadata. Any DbContext that forgot to call `ConfigureConcurrencyStamp()` fails this test.

## 6. Exception & Propagation

### 6.1 `CrestConcurrencyException`

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

### 6.2 Application Service — re-throw white list

`CrestAppServiceBase.UpdateAsync` and `DeleteAsync` catch blocks: add `catch (CrestConcurrencyException) { throw; }` before `catch (Exception)`.

### 6.3 Middleware — 409 mapping

**File**: `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs`

(`CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs` is **excluded from compilation** — only the AspNetCore copy is active.)

```csharp
case CrestConcurrencyException concurrencyEx:
    context.Response.StatusCode = (int)HttpStatusCode.Conflict;
    errorResponse.Code = (int)HttpStatusCode.Conflict;
    errorResponse.Message = "数据已被其他用户修改，请刷新后重试";
    errorResponse.Details = concurrencyEx.Message;
    break;
```

### 6.4 Legacy path: CrudServiceBase

`CrudServiceBase` and `ICrudService` are **legacy** and do not get concurrency support. Mark with:

```
[Obsolete("Use generated CRUD service or CrestAppServiceBase for concurrency support.")]
```

Add a test verifying: `CrudServiceBase.DeleteAsync` does NOT throw `CrestConcurrencyException` for stale-stamp scenarios (it has no concurrency awareness).

## 7. Code Generator Changes

### 7.1 EntitySourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`

| Method | Change |
|--------|--------|
| `GenerateCreateEntityDto` (~line 1082) | Add `p.Name != "ConcurrencyStamp"` to exclusion list |
| `GenerateUpdateEntityDto` (~line 1123) | No change — ConcurrencyStamp is NOT excluded (correct) |
| `GenerateEntityDto` (~line 1045) | No change — includes all properties (correct) |
| `GenerateMappingExtensions` → `UpdateXxxDto.ApplyTo` (~line 1246) | No change — maps ConcurrencyStamp (correct) |

### 7.2 CrudServiceSourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

| Method | Change |
|--------|--------|
| `GenerateCreateEntityDto` (~line 263) | Already excludes ConcurrencyStamp — no change |
| `GenerateUpdateEntityDto` (~line 311) | Remove `"ConcurrencyStamp"` from exclusion list |
| Generated `UpdateAsync` body (~line 580) | Map `input.ConcurrencyStamp` to entity before repository call |
| Generated `DeleteAsync` signature (~line 662) | Add `string? expectedStamp = null` parameter |
| Generated `DeleteAsync` body | If entity has `IHasConcurrencyStamp` AND `expectedStamp` is not null → call `Repository.DeleteAsync(id, expectedStamp)`. Otherwise → existing entity-based path |

Generated DeleteAsync pseudocode:

```csharp
public virtual async Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken ct = default)
{
    if (expectedStamp != null && typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(Book)))
    {
        // True optimistic concurrency: single atomic DELETE + WHERE stamp
        await _repository.DeleteAsync(id, expectedStamp, ct);
        return;
    }

    // Fallback: server-side delete with pre-read (entity-based concurrency)
    var entity = await _repository.GetByIdAsync(id, ct);
    if (entity == null) throw new EntityNotFoundException(typeof(Book), id);
    await _repository.DeleteAsync(entity, ct);
}
```

## 8. Testing

### Test scenarios (EF Core — full coverage)

| # | Test | Description |
|---|------|-------------|
| 1 | `Update_WithCorrectStamp_ShouldSucceed` | Matching stamp → success, new stamp returned |
| 2 | `Update_WithStaleStamp_ShouldThrowConcurrencyException` | Old stamp → `CrestConcurrencyException` |
| 3 | `Update_WithStaleStamp_ShouldReturn409` | API: stale stamp PUT → HTTP 409 |
| 4 | `ConcurrentUpdate_TwoRequests_OneSucceedsOneFails` | Two parallel tasks → one 200, one 409 |
| 5 | `Delete_WithCorrectStamp_ShouldSucceed` | Matching `If-Match` → 204 |
| 6 | `Delete_WithStaleStamp_ShouldThrowConcurrencyException` | Stale `If-Match` → `CrestConcurrencyException` |
| 7 | `Delete_WithStaleStamp_ShouldReturn409` | API: stale `If-Match` DELETE → HTTP 409 |
| 8 | `Entity_WithoutConcurrency_ShouldStillWork` | No `IHasConcurrencyStamp` → old behavior |
| 9 | `NewEntity_GetsConcurrencyStampOnConstruction` | New AuditedEntity → stamp is non-empty GUID |
| 10 | `AllDbContexts_ConcurrencyToken_IsConfigured` | Every DbContext: `IHasConcurrencyStamp` entities have `IsConcurrencyToken == true` |
| 11 | `CrudServiceBase_Delete_NoConcurrencyProtection` | Legacy path: stale stamp doesn't prevent delete |

### Cross-ORM coverage

- EF Core: full coverage.
- FreeSql: scenarios 2 and 6 (update/delete stale stamp → throw).
- SqlSugar: scenarios 2 and 6 (update/delete stale stamp → throw).

## 9. Files Changed

| File | Action |
|------|--------|
| `Domain.Shared/.../IHasConcurrencyStamp.cs` | New |
| `Domain/.../AuditedEntity.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `Domain/.../AuditedAggregateRoot.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `Domain/.../CrestConcurrencyException.cs` | New |
| `Domain/.../CrestRepositoryBase.cs` | Add `DeleteAsync(TKey id, string expectedStamp, ...)` abstract method |
| `Domain/.../ICrestRepositoryBase.cs` | Add `DeleteAsync(TKey id, string expectedStamp, ...)` to interface |
| `OrmProviders.EFCore/.../ModelBuilderExtensions.cs` | New: `ConfigureConcurrencyStamp()` |
| `OrmProviders.EFCore/.../CrestCreatesDbContext.cs` | Call `modelBuilder.ConfigureConcurrencyStamp()` in `OnModelCreating` |
| `OrmProviders.EFCore/.../EfCoreRepository.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `DeleteAsync(TKey, string)` |
| `OrmProviders.EFCore/.../EfCoreRepositoryBase.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `DeleteAsync(TKey, string)` |
| `OrmProviders.FreeSqlProvider/.../FreeSqlRepositoryBase.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `DeleteAsync(TKey, string)` |
| `OrmProviders.SqlSugar/.../SqlSugarRepository.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `DeleteAsync(TKey, string)` |
| `Application.Contracts/.../ICrudAppService.cs` | `DeleteAsync` add `string? expectedStamp = null` parameter |
| `Application/.../CrestAppServiceBase.cs` | Re-throw `CrestConcurrencyException` in `UpdateAsync` / `DeleteAsync` |
| `Application/.../CrudServiceBase.cs` | Mark `[Obsolete]` |
| `Application/.../ICrudService.cs` | Mark `[Obsolete]` |
| `AspNetCore/.../CrudControllerBase.cs` | `DeleteAsync` reads `[FromHeader] If-Match`, passes to service |
| `AspNetCore/.../ExceptionHandlingMiddleware.cs` | Add `CrestConcurrencyException` → 409 case |
| `CodeGenerator/.../EntitySourceGenerator.cs` | Exclude `ConcurrencyStamp` from `CreateXxxDto` |
| `CodeGenerator/.../CrudServiceSourceGenerator.cs` | Include `ConcurrencyStamp` in `UpdateXxxDto`; stamped `DeleteAsync` generation |
| Sample/LibraryDbContext.cs | Call `modelBuilder.ConfigureConcurrencyStamp()` |
| Test projects | 11 scenarios |
