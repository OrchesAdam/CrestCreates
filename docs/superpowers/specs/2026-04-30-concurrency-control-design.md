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
| ORM behavior | All 3 ORMs check old stamp in WHERE clause; 0 rows → exception |
| Error code | HTTP 409, `CONCURRENCY_CONFLICT` |

## 1. Interface Layer

### New: `IHasConcurrencyStamp`

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

### Modified: `AuditedEntity<TId>` and `AuditedAggregateRoot<TId>`

Both add `IHasConcurrencyStamp` and the property:

```csharp
public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
```

- Default value is a fresh GUID — entities get a valid stamp on construction.
- `FullyAuditedEntity`, `FullyAuditedAggregateRoot`, and any custom subclass inherit this automatically.
- `Entity<TId>` and `AggregateRoot<TId>` are **not** modified — pure domain entities without auditing are not forced to carry concurrency stamps.

## 2. Repository Layer

Core rule: if the entity implements `IHasConcurrencyStamp`, `UpdateAsync` uses the old stamp as a concurrency guard. If affected rows = 0, throw `CrestConcurrencyException`.

### EF Core

Use `PropertyEntry.OriginalValue` to add the old stamp to the WHERE clause:

```csharp
var entry = _dbContext.Set<TEntity>().Update(entity);
entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue = oldStamp;
entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).IsModified = true;
await _dbContext.SaveChangesAsync(cancellationToken);
// catch DbUpdateConcurrencyException → throw CrestConcurrencyException
```

**Note**: Requires `ConcurrencyStamp` to be configured as a concurrency token in the EF Core model builder. Add a convention in `OnModelCreating` that detects `IHasConcurrencyStamp` and calls `IsConcurrencyToken()` on the property.

### FreeSql

Use `UpdateDiy` with explicit WHERE on old stamp:

```csharp
var rows = await _orm.Update<TEntity>()
    .SetSource(entity)
    .Where(e => ((IHasConcurrencyStamp)e).ConcurrencyStamp == oldStamp)
    .ExecuteAffrowsAsync(cancellationToken);

if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
```

### SqlSugar

Use `WhereColumns` on the stamp column:

```csharp
var rows = await _sqlSugarClient.Updateable(entity)
    .WhereColumns(it => ((IHasConcurrencyStamp)it).ConcurrencyStamp)
    .ExecuteCommandAsync();

if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
```

### UpdateRangeAsync

Same concurrency logic as `UpdateAsync` — iterate entities and apply stamp-based WHERE for each one that implements `IHasConcurrencyStamp`. If any entity conflicts (0 rows), throw immediately.

### Backward compatibility

Entities that do NOT implement `IHasConcurrencyStamp` continue down the existing `UpdateAsync` code path with no concurrency check.

## 3. Exception

### New: `CrestConcurrencyException`

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

- Base class is `Exception` for now. After the global-exception-handling epic is done, switch to a unified base class.
- HTTP mapping: **409 Conflict**, error code `CONCURRENCY_CONFLICT`.
- The existing exception middleware (if present) is updated to map this exception to 409.

## 4. Code Generator Changes

### DTO generation (`CrudServiceSourceGenerator.cs`)

| DTO | ConcurrencyStamp | Rationale |
|-----|------------------|-----------|
| `GetXxxDto` | Included | Client needs to read the current stamp |
| `CreateXxxDto` | **Excluded** (current behavior) | Entity constructor generates a fresh GUID |
| `UpdateXxxDto` | **Included** (changed) | Client must send back the stamp it read |

### CRUD Service generation

The generated `UpdateAsync` method must map `ConcurrencyStamp` from the `UpdateDto` back to the entity:

```csharp
entity.ConcurrencyStamp = input.ConcurrencyStamp;
```

## 5. Testing

### Test scenarios (EF Core — full coverage)

| Test | Strategy |
|------|----------|
| `Update_WithCorrectStamp_ShouldSucceed` | Normal update with matching stamp → success, stamp refreshed |
| `Update_WithStaleStamp_ShouldThrowConcurrencyException` | Use old stamp → `CrestConcurrencyException` |
| `Update_WithStaleStamp_ShouldReturn409` | API-level test: stale stamp → HTTP 409 |
| `ConcurrentUpdate_SimultaneousRequests` | Two parallel tasks → one succeeds, one gets 409 |
| `Entity_WithoutConcurrency_ShouldStillWork` | Entity w/o `IHasConcurrencyStamp` → old behavior preserved |

### Cross-ORM coverage

- EF Core: full coverage as above.
- FreeSql: one core scenario (stale stamp → throw).
- SqlSugar: one core scenario (stale stamp → throw).

### Test entity

Use existing `Book` entity (`FullyAuditedEntity<Guid>`) to test on the real production entity path.

## 6. Files Changed

| File | Action |
|------|--------|
| `CrestCreates.Domain.Shared/.../IHasConcurrencyStamp.cs` | New |
| `CrestCreates.Domain/.../AuditedEntity.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `CrestCreates.Domain/.../AuditedAggregateRoot.cs` | Add `ConcurrencyStamp` + `IHasConcurrencyStamp` |
| `CrestCreates.Domain/.../CrestConcurrencyException.cs` | New |
| `CrestCreates.OrmProviders.EFCore/.../EfCoreRepository.cs` | Concurrency detection in `UpdateAsync` |
| `CrestCreates.OrmProviders.EFCore/.../EfCoreRepositoryBase.cs` | Concurrency detection in `UpdateAsync` |
| `CrestCreates.OrmProviders.FreeSqlProvider/.../FreeSqlRepositoryBase.cs` | Concurrency detection in `UpdateAsync` |
| `CrestCreates.OrmProviders.SqlSugar/.../SqlSugarRepository.cs` | Concurrency detection in `UpdateAsync` |
| `CrestCreates.CodeGenerator/.../CrudServiceSourceGenerator.cs` | DTO stamp handling + update mapping |
| `CrestCreates.Web/.../ExceptionMiddleware.cs` | 409 mapping (if middleware exists) |
| Test projects | Concurrency conflict tests |
