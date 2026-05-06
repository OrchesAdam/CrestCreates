# CRUD Mainline Design

**Feature plan**: `docs/review/feature-plans/crud-mainline.xml`
**Date**: 2026-05-06
**Status**: draft

This spec defines the generated CRUD mainline. The goal is simple: one entity marker should generate the standard application CRUD surface, and that surface should run through the existing generated Dynamic API, permission, query, UoW, audit, concurrency, and global exception mainlines.

## Decision Summary

| Decision | Choice |
|---|---|
| CRUD mainline | Compile-time generated AppService, contract, DTO, mapper declaration, and permissions. |
| HTTP API | Generated Dynamic API endpoints. Do not make MVC CRUD controllers the mainline. |
| Controller path | `CrudControllerBase` and `CrudControllerSourceGenerator` are compatibility paths only. Do not add new CRUD mainline behavior there. |
| Service shape | Generate directly registered `partial class {Entity}AppService`. Do not generate `{Entity}CrudServiceBase` for inheritance as the default path. |
| Extension model | Use `partial` DTO/service types plus protected virtual hooks. Business code must not edit generated files. |
| Repository | Use `ICrestRepositoryBase<TEntity, TKey>`. Entity-specific repositories are optional custom extensions, not required for generated CRUD. |
| Query model | `{Entity}ListRequestDto : PagedRequestDto`; use `Filters`, `Sorts`, `PageIndex`, and `PageSize`. |
| Generated query fields | Do not auto-generate `Keyword`, first-three-string filters, `StartTime`, or `EndTime`. |
| Mapping | Use generated object mapping. Do not introduce AutoMapper. |
| Permissions | Generate and check `{Entity}.Create`, `{Entity}.Get`, `{Entity}.Search`, `{Entity}.Update`, `{Entity}.Delete`. |
| Write operations | Generated `CreateAsync`, `UpdateAsync`, and `DeleteAsync` must use `[UnitOfWorkMo]`. |
| Concurrency | `UpdateDto` includes `ConcurrencyStamp`; `CreateDto` excludes it; delete supports `expectedStamp` from `If-Match`. |
| Exceptions | Throw platform exceptions and let global exception middleware format responses. |

## 1. Scope

| In Scope | Details |
|---|---|
| Entity marker | A normal entity can opt into generated CRUD through the existing CRUD/entity generation attribute path. |
| DTO generation | Generate output, create, update, and list request DTOs. |
| Contract generation | Generate `I{Entity}AppService` or equivalent service contract that inherits `ICrudAppService`. |
| AppService generation | Generate a concrete partial app service using `ICrestRepositoryBase`. |
| Permission generation | Generate permission names and make generated methods check them. |
| Query execution | Apply pagination, sort descriptors, and filter descriptors through the existing query chain. |
| UoW / audit | Write methods enter UoW and set standard audit / tenant fields through the existing application service logic. |
| Concurrency | Update and delete follow `concurrency-control` design. |
| Dynamic API | Generated CRUD app service is discoverable by `DynamicApiAotSourceGenerator`. |
| Tests | Generator tests plus generated Dynamic API integration tests. |

| Out Of Scope | Reason |
|---|---|
| Enhancing `CrudServiceBase` / `ICrudService` | They are legacy paths. |
| Runtime reflection scanner or executor | Dynamic API mainline is compile-time generated. |
| AutoMapper | Conflicts with generated mapping and AoT direction. |
| MVC controller as the mainline | Would keep two official HTTP paths. |
| Entity-specific generated search fields | The generator cannot safely guess business query semantics. |
| MongoDB CRUD | ORM expansion is separate work. |
| Batch import/export | Separate application feature, not CRUD mainline. |

## 2. Architecture

| Component | Layer | Responsibility |
|---|---|---|
| `GenerateCrudServiceAttribute` / CRUD options on `GenerateEntityAttribute` | `Domain.Shared` | Opt an entity into generated CRUD. |
| `CrudServiceSourceGenerator` | `CodeGenerator` | Generate DTOs, contract, app service, permission declarations, and mapping declarations. |
| `ObjectMappingSourceGenerator` | `CodeGenerator` | Generate entity-to-DTO, create-DTO-to-entity, and update-DTO-apply mapping code. |
| `DynamicApiAotSourceGenerator` | `CodeGenerator` | Discover generated CRUD app services and emit endpoint registry and endpoint handlers. |
| `I{Entity}AppService` | `Application.Contracts` | Strongly typed CRUD contract. |
| `{Entity}AppService` | `Application` | Default generated CRUD implementation. |
| `ICrestRepositoryBase<TEntity, TKey>` | `Domain.Repositories` | Main repository abstraction for generated CRUD. |
| `QueryExecutor` | `Application.Contracts.Query` | Applies generated CRUD list filters, sorts, and paging. |
| Permission definitions | `Application.Contracts` / generated source | Stable CRUD permission names. |
| Global exception middleware | `AspNetCore` | Converts CRUD exceptions into the standard error envelope. |

The generated CRUD service must strengthen the existing mainlines rather than creating another one:

| Concern | Required mainline |
|---|---|
| HTTP | Generated Dynamic API endpoints. |
| Mapping | Generated object mapping. |
| Repository | `ICrestRepositoryBase`. |
| Query | `PagedRequestDto` + `FilterDescriptor` + `SortDescriptor` + `QueryExecutor`. |
| Transaction | `[UnitOfWorkMo]`. |
| Errors | `CrestException` hierarchy + global exception middleware. |

## 3. Generated Files

For entity `Book`, the generator should produce:

| Output | Namespace target | Purpose |
|---|---|---|
| `BookDto.g.cs` | Contracts DTO namespace | Output model. |
| `CreateBookDto.g.cs` | Contracts DTO namespace | Create input model. |
| `UpdateBookDto.g.cs` | Contracts DTO namespace | Update input model. |
| `BookListRequestDto.g.cs` | Contracts DTO namespace | List request model. |
| `IBookAppService.g.cs` | Contracts service namespace | CRUD app service contract. |
| `BookAppService.g.cs` | Application service namespace | Concrete generated app service. |
| `BookCrudPermissions.g.cs` | Contracts permission namespace | Permission constants or definition contributor input. |
| `BookObjectMappings.g.cs` | Application or generated mapping namespace | Mapping declarations consumed by the object mapping generator. |

Do not generate a mainline MVC controller for CRUD. If existing compatibility attributes still generate MVC controllers, they must not be extended by this feature.

## 4. Generated Contract

Generated contract shape:

```csharp
public partial interface IBookAppService
    : ICrudAppService<Guid, BookDto, CreateBookDto, UpdateBookDto, BookListRequestDto>
{
}
```

The contract must be discoverable by the generated Dynamic API path. It should not need a runtime scanner or reflection executor to expose endpoints.

Method behavior:

| Method | HTTP intent through Dynamic API | Permission |
|---|---|---|
| `CreateAsync(CreateBookDto input, CancellationToken ct)` | Create item | `Book.Create` |
| `GetByIdAsync(Guid id, CancellationToken ct)` | Get one item | `Book.Get` |
| `GetListAsync(BookListRequestDto input, CancellationToken ct)` | Search/list items | `Book.Search` |
| `UpdateAsync(Guid id, UpdateBookDto input, CancellationToken ct)` | Update item | `Book.Update` |
| `DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken ct)` | Delete item | `Book.Delete` |

Dynamic API must bind `expectedStamp` from `If-Match` for generated delete endpoints. If this binding is missing in the Dynamic API generator, it is part of this feature because CRUD mainline depends on it.

## 5. DTO Rules

### 5.1 Output DTO

| Field type | Rule |
|---|---|
| `Id` | Include. |
| Business properties | Include unless explicitly excluded. |
| Audit fields | Include when present and not explicitly excluded. |
| Soft delete fields | Exclude from output unless explicitly requested. |
| `ConcurrencyStamp` | Include for entities implementing `IHasConcurrencyStamp`. |
| Navigation properties | Exclude by default unless explicitly supported by an existing safe mapping rule. |
| Domain events / internal framework fields | Always exclude. |

### 5.2 Create DTO

| Field type | Rule |
|---|---|
| `Id` | Exclude. |
| Business settable properties | Include. |
| Creation/modification audit fields | Exclude. |
| Tenant fields | Exclude by default; set from current tenant where applicable. |
| Soft delete fields | Exclude. |
| `ConcurrencyStamp` | Exclude. |
| Domain events / internal framework fields | Always exclude. |

### 5.3 Update DTO

| Field type | Rule |
|---|---|
| `Id` | Prefer route id as source of truth. If retained in DTO for compatibility, validate it matches the route id. |
| Business updatable properties | Include. |
| Creation audit fields | Exclude. |
| Modification audit fields | Exclude; set by framework. |
| Tenant fields | Exclude; tenant ownership must not be changed by update DTO. |
| Soft delete fields | Exclude. |
| `ConcurrencyStamp` | Include for entities implementing `IHasConcurrencyStamp`. |

### 5.4 List Request DTO

Generated list request must be simple:

```csharp
public partial class BookListRequestDto : PagedRequestDto
{
}
```

The generator must not add guessed fields such as:

| Field | Reason |
|---|---|
| `Keyword` | Entity search semantics are business-specific. |
| First few string properties | Produces arbitrary and inconsistent APIs. |
| `StartTime` / `EndTime` | Assumes every entity uses creation-time range search. |

Business-specific query fields may be added manually through partial DTOs later, but the generated CRUD mainline only guarantees descriptor-based filtering and sorting.

## 6. AppService Behavior

Generated app service shape:

```csharp
public partial class BookAppService : IBookAppService
{
    private readonly ICrestRepositoryBase<Book, Guid> _repository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICurrentUser _currentUser;
    private readonly IDataPermissionFilter _dataPermissionFilter;

    // Generated CRUD methods...
}
```

The service must be directly registered by the existing DI/module generation path. It must not require a hand-written subclass to become usable.

### 6.1 Create

| Step | Behavior |
|---|---|
| Validate input | Null input throws argument/validation exception. |
| Permission | Check `{Entity}.Create`. |
| Map | `CreateDto -> Entity` through generated mapper. |
| Audit / tenant | Set creation audit fields and tenant fields using current context. |
| Persist | `ICrestRepositoryBase.InsertAsync`. |
| Return | Map entity to DTO. |
| Transaction | Method has `[UnitOfWorkMo]`. |

### 6.2 Get By Id

| Step | Behavior |
|---|---|
| Permission | Check `{Entity}.Get`. |
| Query | Use repository queryable and data permission filter. |
| Not found | Throw `CrestEntityNotFoundException`. |
| Ownership | Validate tenant/data ownership before returning. |
| Return | Map entity to DTO. |

`GetByIdAsync` should not silently return `null` for the generated mainline. A missing entity is a platform 404 response through global exception handling.

### 6.3 Get List

| Step | Behavior |
|---|---|
| Permission | Check `{Entity}.Search`. |
| Base query | `Repository.GetQueryable()`. |
| Data permission | Apply existing data permission filter. |
| Filters | Apply `input.Filters` through `QueryExecutor`. |
| Sorts | Apply `input.Sorts` through `QueryExecutor`. |
| Paging | Apply `PageIndex` and `PageSize`. |
| Return | `PagedResultDto<TDto>`. |

Filter and sort fields must be validated against generated allowlists before applying expressions. Unknown fields should fail with a business/validation exception instead of producing unsafe or provider-specific expression errors.

### 6.4 Update

| Step | Behavior |
|---|---|
| Permission | Check `{Entity}.Update`. |
| Load | Query entity by route id. |
| Not found | Throw `CrestEntityNotFoundException`. |
| Ownership | Validate tenant/data ownership. |
| Concurrency | For `IHasConcurrencyStamp`, require and validate `input.ConcurrencyStamp`. |
| Map | Apply update DTO through generated `ApplyTo`. |
| Audit | Set modification audit fields. |
| Persist | `ICrestRepositoryBase.UpdateAsync`. |
| Return | Updated DTO. |
| Transaction | Method has `[UnitOfWorkMo]`. |

The update mapper must not wipe the tracked entity's original concurrency token before the repository performs its optimistic concurrency check. It should pass the expected stamp through the existing repository/concurrency design rather than silently overwriting state.

### 6.5 Delete

| Step | Behavior |
|---|---|
| Permission | Check `{Entity}.Delete`. |
| Concurrency entity | If entity implements `IHasConcurrencyStamp`, `expectedStamp` is required. |
| Missing stamp | Throw `CrestPreconditionRequiredException` and return 428 through middleware. |
| Stale stamp | Repository throws `CrestConcurrencyException` and returns 409 through middleware. |
| Non-concurrency entity | Delete without stamp after ownership validation. |
| Transaction | Method has `[UnitOfWorkMo]`. |

For generated Dynamic API endpoints, `expectedStamp` must be bound from the `If-Match` header for delete.

## 7. Extension Hooks

Generated services are partial and provide protected virtual hooks:

| Hook | When |
|---|---|
| `OnCreatingAsync(entity, input, ct)` | Before insert. |
| `OnCreatedAsync(entity, ct)` | After insert. |
| `OnUpdatingAsync(entity, input, ct)` | Before update mapper applies changes. |
| `OnUpdatedAsync(entity, ct)` | After update. |
| `OnDeletingAsync(entity, ct)` | Before delete. |
| `OnDeletedAsync(id, ct)` | After delete. |
| `ConfigureListQueryAsync(query, input, ct)` | Before descriptor filters/sorts are applied. |
| `ValidateCreateAsync(input, ct)` | Before create mapping. |
| `ValidateUpdateAsync(id, input, ct)` | Before update mapping. |

Rules:

| Rule | Reason |
|---|---|
| Hooks must be optional. | A generated service should work with no hand-written code. |
| Hooks must not require reflection. | AoT friendliness. |
| Hooks must not bypass permissions or UoW. | CRUD security and transaction behavior stay centralized. |
| Hooks must not replace generated mainline methods by default. | Avoid another inheritance-based path. |

## 8. Permission Model

Generated permission names:

| Operation | Permission |
|---|---|
| Create | `{Entity}.Create` |
| Get one | `{Entity}.Get` |
| Search/list | `{Entity}.Search` |
| Update | `{Entity}.Update` |
| Delete | `{Entity}.Delete` |

Compatibility note:

| Existing name | New CRUD mainline rule |
|---|---|
| `{Entity}.View` | Do not use for generated CRUD. Keep only as legacy compatibility if already present. |
| `{Entity}.Export` | Out of scope for CRUD mainline. |

The permission generator must avoid duplicate registrations and must fail clearly if two generated definitions for the same permission disagree on display metadata.

## 9. Query Rules

Generated CRUD list APIs use descriptor-based querying.

| Input | Rule |
|---|---|
| `PageIndex` | Clamp through `PagedRequestDto`. |
| `PageSize` | Clamp through `PagedRequestDto.MaxPageSize`. |
| `Filters` | Apply in order after validation. |
| `Sorts` | Apply in order after validation. |
| Empty sorts | Use deterministic default sort, preferably `Id` or `CreationTime desc` when available. |

Allowed filter/sort fields are generated from entity properties:

| Property type | Filter | Sort |
|---|---|---|
| Primitive scalar | Allowed. | Allowed. |
| `string` | Allowed. | Allowed. |
| `DateTime` / `DateTimeOffset` | Allowed. | Allowed. |
| enum | Allowed. | Allowed. |
| navigation object | Not allowed by default. | Not allowed by default. |
| collection | Not allowed. | Not allowed. |
| domain event/internal field | Not allowed. | Not allowed. |
| soft delete field | Not allowed by default. | Not allowed by default. |

Unsupported filter operators for a field type should fail with a clear `CrestValidationException` or `CrestBusinessException`.

## 10. Mapping Rules

CRUD generation must emit mapping declarations for the object mapping generator:

| Mapping | Direction |
|---|---|
| `Entity -> EntityDto` | Output. |
| `CreateEntityDto -> Entity` | Create. |
| `UpdateEntityDto -> Entity` | Apply/update. |

Rules:

| Rule | Required behavior |
|---|---|
| Inherited properties | Include inherited public properties when generating DTOs and mappings. |
| `ConcurrencyStamp` | Include in output/update mapping where required; exclude from create mapping. |
| `DomainEvents` | Never map to DTOs. |
| Ignored properties | Respect existing map ignore attributes. |
| No AutoMapper | Generated methods only. |

## 11. Dynamic API Integration

Generated CRUD AppServices must be visible to `DynamicApiAotSourceGenerator`.

| Requirement | Passing condition |
|---|---|
| Generated registry | CRUD service appears in `GeneratedDynamicApiRegistry.g.cs`. |
| Generated endpoints | CRUD methods appear in `GeneratedDynamicApiEndpoints.g.cs`. |
| Body binding | Create/update DTOs bind from body. |
| Query binding | List request binds from query. |
| Route binding | `id` binds from route. |
| Header binding | `expectedStamp` binds from `If-Match` for delete. |
| UoW | Generated endpoints preserve `[UnitOfWorkMo]` transaction metadata. |
| Swagger | CRUD endpoints and DTO schemas appear through generated registry metadata. |

Do not add CRUD behavior to `DynamicApiScanner`, `DynamicApiEndpointExecutor`, or runtime reflection fallback.

## 12. Error Handling

Generated CRUD must throw framework exceptions instead of building ad hoc responses.

| Scenario | Exception | HTTP through middleware |
|---|---|---|
| Entity not found | `CrestEntityNotFoundException` | 404 |
| Missing permission | existing permission exception | 401 / 403 |
| Missing delete `If-Match` | `CrestPreconditionRequiredException` | 428 |
| Stale concurrency stamp | `CrestConcurrencyException` | 409 |
| Invalid filter/sort field | `CrestValidationException` or `CrestBusinessException` | 400 |
| Unsupported filter operator | `CrestValidationException` or `CrestBusinessException` | 400 |
| Validation failure | `CrestValidationException` | 400 |

Generated CRUD code must not wrap platform exceptions into generic `Exception`, because that breaks the global exception response contract.

## 13. Audit And Tenant Behavior

| Concern | Rule |
|---|---|
| Creation audit | Set creator and creation time through existing framework logic. |
| Modification audit | Set modifier and modification time through existing framework logic. |
| Tenant assignment | For tenant-owned entities, set tenant id from current tenant/current user, not from DTO input. |
| Tenant isolation | Reads and writes apply existing tenant/data permission filters. |
| Ownership validation | Update/delete must verify the loaded entity belongs to the current allowed scope. |
| Audit logs | Rely on existing UoW/audit mainline; do not create CRUD-specific audit tables. |

## 14. Legacy Cleanup Rules

| Item | Action |
|---|---|
| `CrudServiceBase` | Keep obsolete; do not add generated CRUD mainline behavior. |
| `ICrudService` | Keep obsolete; do not extend. |
| `CrudControllerBase` | Keep compatibility; do not add new mainline features beyond already required bug fixes. |
| `CrudControllerSourceGenerator` | Do not use as acceptance path for this feature. |
| Existing controller tests | Keep only to prevent breaking compatibility; new CRUD feature tests should target generated AppService and Dynamic API. |

If implementation finds active sample or tests depending on MVC-generated CRUD controllers, migrate them to generated Dynamic API where feasible, or explicitly mark them as compatibility tests.

## 15. Sample Migration

The LibraryManagement sample should demonstrate the new mainline with one simple entity.

| Step | Expected result |
|---|---|
| Mark sample entity for generated CRUD | DTOs, contract, AppService, permissions are generated. |
| Remove hand-written duplicate CRUD code for that entity where possible | Sample proves default path needs little code. |
| Use generated Dynamic API endpoint | HTTP integration test hits generated endpoint, not MVC controller. |
| Keep custom business service separately | Non-CRUD behavior remains hand-written. |

The sample should not rely on runtime reflection fallback or generated MVC CRUD controller as the proof path.

## 16. Testing Strategy

| Layer | Test | Verifies |
|---|---|---|
| CodeGenerator.Tests | `GeneratedCrud_ShouldCompileForSampleEntity` | One marked entity produces compilable DTOs, contract, AppService, permissions, mappings. |
| CodeGenerator.Tests | `GeneratedCrud_ShouldUseICrestRepositoryBase` | Generated service depends on `ICrestRepositoryBase<TEntity,TKey>`, not entity-specific repository as a requirement. |
| CodeGenerator.Tests | `GeneratedCrud_ListRequest_ShouldOnlyUsePagedRequestDtoDescriptors` | No generated `Keyword`, guessed string filters, or time range fields. |
| CodeGenerator.Tests | `GeneratedCrud_ShouldGeneratePermissions` | `{Entity}.Create/Get/Search/Update/Delete` are generated. |
| CodeGenerator.Tests | `GeneratedCrud_UpdateDto_ShouldIncludeConcurrencyStamp` | Concurrency entities get stamp in update DTO. |
| CodeGenerator.Tests | `GeneratedCrud_CreateDto_ShouldExcludeConcurrencyStamp` | Create DTO never accepts stamp. |
| CodeGenerator.Tests | `GeneratedCrud_Delete_ShouldRequireExpectedStampForConcurrentEntity` | Generated delete enforces 428 path. |
| CodeGenerator.Tests | `GeneratedCrud_ShouldNotWrapPlatformExceptions` | Generated code rethrows platform exceptions. |
| Application.Tests | `GeneratedCrud_GetList_ShouldApplyFiltersAndSorts` | Query descriptors work through `QueryExecutor`. |
| Application.Tests | `GeneratedCrud_InvalidFilterField_ShouldFailClearly` | Unknown filter/sort fields fail with platform exception. |
| IntegrationTests | `GeneratedCrud_ShouldUseDynamicApiGeneratedEndpoint` | HTTP CRUD endpoint is generated Dynamic API path. |
| IntegrationTests | `GeneratedCrud_WriteOperations_ShouldUseUnitOfWork` | Failed write rolls back. |
| IntegrationTests | `GeneratedCrud_DeleteConcurrentEntity_RequiresIfMatch` | Missing `If-Match` returns 428. |
| IntegrationTests | `GeneratedCrud_DeleteConcurrentEntity_StaleStampReturnsConflict` | Stale stamp returns 409. |
| IntegrationTests | `GeneratedCrud_ShouldCheckPermissions` | User without permission cannot call generated CRUD. |
| Compatibility tests | `CrudServiceBase_ShouldRemainLegacy` | Legacy path is not accidentally treated as mainline. |

Testing priority:

| Priority | Reason |
|---|---|
| Generated code compile tests | Catch generator regressions quickly. |
| Generated Dynamic API integration tests | Prove the official HTTP path. |
| Query and concurrency tests | Cover the highest-risk behavior. |
| Controller compatibility tests | Keep minimal and not feature-expanding. |

## 17. Acceptance Criteria

| Criterion | Passing condition |
|---|---|
| One marker generates full CRUD surface | Marked entity produces DTOs, contract, AppService, permissions, mappings, and Dynamic API endpoints. |
| Generated path is AoT-friendly | No runtime reflection scanner/executor is needed for CRUD exposure. |
| Query descriptors work | `Filters`, `Sorts`, and paging are applied and tested. |
| Permissions are checked | All five CRUD methods check the expected generated permission. |
| Write methods use UoW | Create/update/delete are transactional. |
| Concurrency works | Update/delete behavior matches `concurrency-control` design. |
| Errors use global middleware | CRUD exceptions become standard platform error responses. |
| Legacy path stays legacy | `CrudServiceBase` / MVC generated controllers are not the new acceptance path. |
| Sample proves the mainline | Sample CRUD endpoint works through generated Dynamic API. |

## 18. Review Checklist

| Check | Expected answer |
|---|---|
| Does the generated CRUD API require runtime reflection? | No. |
| Does the generated CRUD AppService use `ICrestRepositoryBase`? | Yes. |
| Does the list request rely on descriptor query, not guessed fields? | Yes. |
| Are permissions named `Create/Get/Search/Update/Delete`? | Yes. |
| Does delete bind `If-Match` on the generated Dynamic API path? | Yes. |
| Are platform exceptions preserved? | Yes. |
| Did we avoid adding new behavior to `CrudServiceBase`? | Yes. |
| Did we avoid making MVC controller generation the mainline? | Yes. |
| Do tests verify generated Dynamic API, not runtime fallback? | Yes. |

