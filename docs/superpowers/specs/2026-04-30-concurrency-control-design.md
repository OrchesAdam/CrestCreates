# Concurrency Control Design

**Date**: 2026-04-30
**Scope**: 乐观并发控制主链（单一方案，不要多套并存）

## Decision Summary

| Decision | Choice |
|----------|--------|
| Strategy | Optimistic concurrency (optimistic lock) |
| Field type | `string ConcurrencyStamp` (Guid-based) |
| Entity scope | `AuditedEntity<TId>` and `AuditedAggregateRoot<TId>` (and all subclasses) |
| DTO exposure | GetDto includes stamp; UpdateDto includes stamp (client round-trips it); CreateDto excludes stamp |
| ORM behavior | All 3 ORMs: update/delete WHERE includes expected stamp; 0 rows → throw |
| Delete stamp source | Server-side read (entity's DB-current stamp within same UoW). Client-provided stamp (If-Match) is a future enhancement. |
| Error code | HTTP 409, `CONCURRENCY_CONFLICT` |
| Operations | Update and Delete both check concurrency stamp |
| Legacy path | `CrudServiceBase` / `ICrudService` — no concurrency support. Official main chain is `CrestAppServiceBase` / generated CRUD service path only. |

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

- Default is a fresh GUID — new entities get a valid stamp on construction.
- `FullyAuditedEntity`, `FullyAuditedAggregateRoot`, and custom subclasses inherit automatically.
- `Entity<TId>` and `AggregateRoot<TId>` are **not** modified.

### 1.3 Entity Scope Inventory

Covered (inherit from `AuditedEntity` / `AuditedAggregateRoot`):

| Entity | Base class | Concurrency? |
|--------|-----------|:---:|
| `Tenant` | AuditedAggregateRoot | ✓ |
| `SettingValue` | AuditedAggregateRoot | ✓ |
| `FeatureValue` | AuditedAggregateRoot | ✓ |
| User entities via `[Entity]` with `FullyAuditedEntity<TId>` base | FullyAuditedEntity | ✓ |

Not covered (inherit from `Entity<TId>` or `MustHaveTenantOrganizationEntity<TId>` directly):

| Entity | Base class | Note |
|--------|-----------|------|
| `Permission` | Entity<Guid> | Infrastructure table, rarely user-modified |
| `PermissionGrant` | Entity<Guid> | Infrastructure table |
| `Role` | MustHaveTenantOrganizationEntity<Guid> | Has audit fields inline, not via AuditedEntity |
| `User` | MustHaveTenantOrganizationEntity<Guid> | Has audit fields inline |
| `Organization` | MustHaveTenantOrganizationEntity<Guid> | Has audit fields inline |
| `RefreshToken` | Entity<Guid> | Infrastructure |
| `AuditLog` | Entity<Guid> | Write-only, no updates |
| `IdentitySecurityLog` | Entity<Guid> | Write-only |
| `TenantConnectionString` | Entity<Guid> | Internal to Tenant |
| `TenantDomainMapping` | Entity<Guid> | Internal to Tenant |
| `DataPermission` | Entity<Guid> | Infrastructure |
| `RolePermission` | Entity<Guid> | Infrastructure |
| `UserRole` | Entity<Guid> | Infrastructure |

Infrastructure/internal entities don't need optimistic concurrency. An entity that needs it but doesn't inherit from AuditedEntity can add `IHasConcurrencyStamp` manually.

## 2. Stamp Lifecycle

### Update flow (client round-trip)

```
Client reads GetDto → ConcurrencyStamp = "abc-123"
  ↓
Client sends PUT /x/5 with UpdateDto { ..., ConcurrencyStamp = "abc-123" }
  ↓
AppService: Repository.GetAsync(5) → entity (ConcurrencyStamp = "def-456" from DB)
AppService: MapToEntity(input, entity) → entity.ConcurrencyStamp = "abc-123"  ← expected stamp from client
  ↓
Repository.UpdateAsync(entity):
  oldStamp = entity.ConcurrencyStamp               // "abc-123"  ← expected
  newStamp = Guid.NewGuid().ToString()              // "ghi-789"  ← new
  entity.ConcurrencyStamp = newStamp                // set new value on entity
  UPDATE ... SET [all columns], ConcurrencyStamp = 'ghi-789'
       WHERE Id = 5 AND ConcurrencyStamp = 'abc-123'
  ↓
  rows == 1 → success, returned entity has ConcurrencyStamp = "ghi-789"
  rows == 0 → throw CrestConcurrencyException
```

**Rules**:
- `UpdateDto.ConcurrencyStamp` is the **expected stamp** (what the client read and sends back).
- `MapToEntity` / `ApplyTo` copies it to `entity.ConcurrencyStamp` verbatim.
- Repository reads `entity.ConcurrencyStamp` as the expected stamp for the WHERE clause.
- Repository **always generates a new stamp** before persisting. Every successful update changes the stamp.
- Returned DTO contains the new stamp — client must use it for the next update.

### Delete flow (server-side stamp from read)

```
Client sends DELETE /x/5
  ↓
AppService: Repository.GetAsync(5) → entity (ConcurrencyStamp = "def-456" from DB)
  ↓
Repository.DeleteAsync(entity):
  DELETE FROM X WHERE Id = 5 AND ConcurrencyStamp = 'def-456'
  ↓
  rows == 1 → success
  rows == 0 → throw CrestConcurrencyException
```

**Rules**:
- The entity is read from DB within the same request/UoW, providing the current stamp.
- Repository uses `entity.ConcurrencyStamp` as the expected stamp in the DELETE WHERE clause.
- No API contract changes needed — `ICrudAppService.DeleteAsync(id)` and `HttpDelete("{id}")` unchanged.
- The UoW transaction ensures atomicity between the read and the delete within one request.
- If stricter client-provided stamp is needed later (e.g., `If-Match` header), it can be added as an enhancement without changing the repository or entity layer.

## 3. Repository Layer

### 3.1 UpdateAsync

```csharp
if (entity is IHasConcurrencyStamp stamp)
{
    var oldStamp = stamp.ConcurrencyStamp;          // expected
    stamp.ConcurrencyStamp = Guid.NewGuid().ToString(); // new
    // execute update with WHERE ConcurrencyStamp = oldStamp
    // if rows == 0 → throw CrestConcurrencyException
}
else
{
    // existing code path, no concurrency check
}
```

#### EF Core

```csharp
if (entity is IHasConcurrencyStamp)
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

#### FreeSql

```csharp
if (entity is IHasConcurrencyStamp)
{
    var rows = await _orm.Update<TEntity>()
        .SetSource(entity)
        .Where("Id = {0} AND ConcurrencyStamp = {1}", entity.Id, oldStamp)
        .ExecuteAffrowsAsync(cancellationToken);

    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
}
else
{
    await _orm.Update<TEntity>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);
}
```

#### SqlSugar

```csharp
if (entity is IHasConcurrencyStamp)
{
    var rows = await _sqlSugarClient.Updateable(entity)
        .Where("Id = @Id AND ConcurrencyStamp = @OldStamp",
               new { Id = entity.Id, OldStamp = oldStamp })
        .ExecuteCommandAsync();

    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
}
else
{
    await _sqlSugarClient.Updateable(entity).ExecuteCommandAsync();
}
```

### 3.2 DeleteAsync(TEntity entity) — concurrency-aware

The existing entity-based overload gains concurrency detection internally. **No interface change needed.**

```csharp
// All three ORMs follow this pattern:
if (entity is IHasConcurrencyStamp stamp)
{
    // DELETE FROM X WHERE Id = @id AND ConcurrencyStamp = @entityStamp
    var rows = /* ORM-specific delete */;
    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
}
else
{
    // existing DELETE BY ID path, no concurrency
}
```

#### EF Core (entity-based delete with concurrency)

```csharp
if (entity is IHasConcurrencyStamp)
{
    var entry = _dbContext.Set<TEntity>().Remove(entity);
    // OriginalValue on a concurrency token adds it to the DELETE WHERE clause
    entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue
        = entity.ConcurrencyStamp;
}
else
{
    _dbContext.Set<TEntity>().Remove(entity);
}
await _dbContext.SaveChangesAsync(cancellationToken);
// DbUpdateConcurrencyException → throw new CrestConcurrencyException(...)
```

#### FreeSql

```csharp
if (entity is IHasConcurrencyStamp stamp)
{
    var rows = await _orm.Delete<TEntity>()
        .Where("Id = {0} AND ConcurrencyStamp = {1}", entity.Id, stamp.ConcurrencyStamp)
        .ExecuteAffrowsAsync(cancellationToken);
    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
}
else
{
    await _orm.Delete<TEntity>().Where(e => e.Id.Equals(entity.Id)).ExecuteAffrowsAsync(cancellationToken);
}
```

#### SqlSugar

```csharp
if (entity is IHasConcurrencyStamp stamp)
{
    var rows = await _sqlSugarClient.Deleteable<TEntity>()
        .Where("Id = @Id AND ConcurrencyStamp = @Stamp",
               new { Id = entity.Id, Stamp = stamp.ConcurrencyStamp })
        .ExecuteCommandAsync();
    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
}
else
{
    await _sqlSugarClient.Deleteable(entity).ExecuteCommandAsync();
}
```

### 3.3 DeleteAsync(TKey id) — no concurrency (documented limitation)

The id-based delete overload does **not** benefit from concurrency checking because it has no entity context to read the stamp from. Callers needing concurrency on delete should use the entity-based overload (which the generated CRUD service already does).

### 3.4 UpdateRangeAsync (Batch Update)

Each entity that implements `IHasConcurrencyStamp` gets its own new stamp and old-stamp WHERE check.

**Transaction requirement**: UpdateRangeAsync requires an ambient transaction/UoW. If none is active, throw `InvalidOperationException` (not attempt to create one — each ORM has different transaction APIs, and the calling code should already be within a `[UnitOfWorkMo]` scope from the app service layer).

```csharp
// EF Core
if (_dbContext.CurrentTransaction == null)
    throw new InvalidOperationException("UpdateRangeAsync with concurrency requires an active transaction/UoW.");

// FreeSql — check UnitOfWorkManager
// SqlSugar — check sqlSugarClient.Ado.Transaction
```

If any entity conflicts (0 rows), the exception aborts the transaction (no partial updates).

## 4. EF Core Model Configuration

### 4.1 ConfigureConcurrencyStamp extension

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

Without `IsConcurrencyToken()`, EF Core does not include `ConcurrencyStamp` in the UPDATE/DELETE WHERE clause and does not throw `DbUpdateConcurrencyException` — silent overwrite.

### 4.2 Where it's called

`CrestCreatesDbContext.OnModelCreating` calls `modelBuilder.ConfigureConcurrencyStamp()` at the end of its override. Sample DbContexts that inherit from `CrestCreatesDbContext` and call `base.OnModelCreating()` get it automatically. Custom standalone DbContexts must call it manually.

### 4.3 Safety net test

A test verifies: given an entity implementing `IHasConcurrencyStamp`, if `ConfigureConcurrencyStamp()` was NOT called on the model, the test fails. This catches any DbContext that forgot the call.

```csharp
// Pseudocode — the test registers a small DbContext with an IHasConcurrencyStamp entity
// but deliberately omits ConfigureConcurrencyStamp(), then verifies
// DbUpdateConcurrencyException is NOT thrown (i.e., the test asserts broken behavior
// and uses a separate check for production contexts).
```

## 5. Exception & Propagation

### 5.1 New: `CrestConcurrencyException`

**File**: `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs`

```csharp
public class CrestConcurrencyException : Exception
{
    public string EntityType { get; }
    public object? EntityId { get; }

    public CrestConcurrencyException(string entityType, object? entityId)
        : base($"Concurrency conflict: {entityType} (Id={entityId}) "
             + "has been modified by another user.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
```

- Base class is `Exception` for now. After global-exception-handling epic, switch to a unified base class.

### 5.2 Application Service: Re-throw White List

`CrestAppServiceBase.UpdateAsync` (line 243) wraps all unhandled exceptions in a generic `Exception`. Add `CrestConcurrencyException` to the re-throw list alongside `KeyNotFoundException`:

```csharp
// In CrestAppServiceBase.UpdateAsync:
catch (KeyNotFoundException) { throw; }
catch (CrestConcurrencyException) { throw; }   // ← ADD — must not be wrapped
catch (DbException ex) { throw new Exception($"...", ex); }
catch (Exception ex) { throw new Exception($"...", ex); }

// In CrestAppServiceBase.DeleteAsync: same treatment
```

`CrudServiceBase` (legacy path) — does NOT get concurrency support. Its `UpdateAsync` / `DeleteAsync` remain unchanged. Entities used through `CrudServiceBase` don't benefit from concurrency protection.

### 5.3 Exception Middleware: 409 Mapping

**File**: `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs`

(Note: the `CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs` copy is **excluded from compilation** in the `.csproj` — the AspNetCore one is the real middleware.)

Add to the switch statement:

```csharp
case CrestConcurrencyException concurrencyEx:
    context.Response.StatusCode = (int)HttpStatusCode.Conflict;  // 409
    errorResponse.Code = (int)HttpStatusCode.Conflict;
    errorResponse.Message = "数据已被其他用户修改，请刷新后重试";
    errorResponse.Details = concurrencyEx.Message;
    logger.LogWarning(concurrencyEx,
        "Concurrency conflict for request {TraceId}", context.TraceIdentifier);
    break;
```

## 6. Code Generator Changes

### 6.1 EntitySourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`

| Method | Line | Change |
|--------|------|--------|
| `GenerateCreateEntityDto` | ~1082 | Add `p.Name != "ConcurrencyStamp"` to exclusion list |
| `GenerateUpdateEntityDto` | ~1123 | No change — does NOT exclude ConcurrencyStamp (correct) |
| `GenerateEntityDto` | ~1045 | No change — includes all properties (correct) |
| `GenerateMappingExtensions` → `UpdateXxxDto.ApplyTo` | ~1246 | No change — ConcurrencyStamp passes the writable filter and is mapped (correct) |

The `ApplyTo` for UpdateDto maps `ConcurrencyStamp` from DTO to entity ($3.1 in the flow: this is the expected stamp).

### 6.2 CrudServiceSourceGenerator

**File**: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

| Method | Line | Change |
|--------|------|--------|
| `GenerateCreateEntityDto` | ~263 | Already excludes ConcurrencyStamp — no change |
| `GenerateUpdateEntityDto` | ~311 | Remove `"ConcurrencyStamp"` from exclusion list |
| Generated `UpdateAsync` body | ~580 | Ensure `entity.ConcurrencyStamp = input.ConcurrencyStamp` is mapped from DTO before repository call |
| Generated `DeleteAsync` body | ~662-670 | Already uses entity-based delete (`Repository.DeleteAsync(entity)`) — no change needed. Repository handles concurrency internally via entity's DB-read stamp. |

Generated UpdateAsync mapping pseudocode:

```csharp
public virtual async Task<BookDto> UpdateAsync(Guid id, UpdateBookDto input, CancellationToken ct)
{
    var entity = await _repository.GetAsync(id, ct);
    // ... permission check, null check ...
    entity = input.ApplyTo(entity);  // ← copies input.ConcurrencyStamp → entity.ConcurrencyStamp (expected)
    // ... audit properties ...
    var result = await _repository.UpdateAsync(entity, ct);  // ← generates new stamp internally
    return result.ToDto();
}
```

## 7. Testing

### Test scenarios (EF Core — full coverage)

| # | Test | Description |
|---|------|-------------|
| 1 | `Update_WithCorrectStamp_ShouldSucceed` | Matching stamp → success, returned DTO has new stamp ≠ old |
| 2 | `Update_WithStaleStamp_ShouldThrowConcurrencyException` | Old stamp → throw, entity unchanged |
| 3 | `Update_WithStaleStamp_ShouldReturn409` | API-level: stale stamp PUT → HTTP 409 |
| 4 | `ConcurrentUpdate_TwoRequests_OneSucceedsOneFails` | Two parallel tasks → one 200, one 409 |
| 5 | `Delete_WithCurrentEntityStamp_ShouldSucceed` | Read+delete within UoW → success |
| 6 | `Delete_AfterConcurrentModification_ShouldThrow` | Entity modified between read and delete → throw |
| 7 | `Entity_WithoutConcurrency_ShouldStillWork` | Entity w/o `IHasConcurrencyStamp` → old behavior |
| 8 | `NewEntity_GetsConcurrencyStampOnConstruction` | New `AuditedEntity` → stamp is valid GUID |
| 9 | `DbContext_MissingConcurrencyConfig_ShouldBeDetected` | Safety net: ensure `ConfigureConcurrencyStamp` call is verifiable |

### Cross-ORM coverage

- EF Core: full coverage (all 9 scenarios).
- FreeSql: scenarios 2 and 6 (update/delete stale stamp → throw).
- SqlSugar: scenarios 2 and 6 (update/delete stale stamp → throw).

### Test entity

Use existing `Book` entity (`FullyAuditedEntity<Guid>`) — real production entity path.

## 8. Files Changed

| File | Action |
|------|--------|
| `CrestCreates.Domain.Shared/.../IHasConcurrencyStamp.cs` | New |
| `CrestCreates.Domain/.../AuditedEntity.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `CrestCreates.Domain/.../AuditedAggregateRoot.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `CrestCreates.Domain/.../CrestConcurrencyException.cs` | New |
| `CrestCreates.OrmProviders.EFCore/.../ModelBuilderExtensions.cs` | New: `ConfigureConcurrencyStamp()` |
| `CrestCreates.OrmProviders.EFCore/.../CrestCreatesDbContext.cs` | Call `modelBuilder.ConfigureConcurrencyStamp()` in `OnModelCreating` |
| `CrestCreates.OrmProviders.EFCore/.../EfCoreRepository.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `UpdateRangeAsync` |
| `CrestCreates.OrmProviders.EFCore/.../EfCoreRepositoryBase.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `UpdateRangeAsync` |
| `CrestCreates.OrmProviders.FreeSqlProvider/.../FreeSqlRepositoryBase.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `UpdateRangeAsync` |
| `CrestCreates.OrmProviders.SqlSugar/.../SqlSugarRepository.cs` | Concurrency in `UpdateAsync`, `DeleteAsync(TEntity)`, `UpdateRangeAsync` |
| `CrestCreates.Application/.../CrestAppServiceBase.cs` | Re-throw `CrestConcurrencyException` in `UpdateAsync` / `DeleteAsync` |
| `CrestCreates.AspNetCore/.../ExceptionHandlingMiddleware.cs` | Add `CrestConcurrencyException` → 409 case |
| `CrestCreates.CodeGenerator/.../EntitySourceGenerator.cs` | Exclude `ConcurrencyStamp` from `CreateXxxDto` |
| `CrestCreates.CodeGenerator/.../CrudServiceSourceGenerator.cs` | Include `ConcurrencyStamp` in `UpdateXxxDto`; map stamp before update |
| Test projects | Concurrency conflict tests (9 scenarios) |
