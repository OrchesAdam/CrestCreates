# DTO Mapping Design

**Feature plan**: `docs/review/feature-plans/dto-mapping.xml`
**Date**: 2026-05-07
**Status**: draft

This spec defines DTO mapping as a compile-time generated mainline. The goal is to make object mapping predictable, AoT-friendly, and directly usable by generated CRUD without introducing AutoMapper or a runtime mapping fallback.

## Decision Summary

| Decision | Choice |
|---|---|
| Mainline | Use `ObjectMappingSourceGenerator` to emit static `ToTarget` and `Apply` methods. |
| Runtime mapper | Do not introduce AutoMapper, reflection mapping, or a runtime converter registry. |
| Scope | Close and harden the existing generator path rather than designing a new mapping platform. |
| CRUD integration | Generated CRUD calls `{Entity}ObjectMappings.ToTarget(...)` and `Apply(...)` only. |
| Attribute ownership | DTO properties are the preferred place for `MapFrom`, `MapName`, and `MapIgnore`. |
| Entity attributes | Entity-side mapping attributes are allowed only where already supported, but DTO-side declarations are the documented mainline. |
| Update safety | `Apply` must not overwrite tenant, audit, soft-delete, identity, or concurrency fields. |
| Navigation mapping | No implicit deep scanning. Navigation paths are supported only through explicit `MapFrom("A.B")`. |
| Type conversion | Support a small static set of safe conversions; require `MapConvert` for anything format-sensitive. |
| Errors | Invalid mappings produce Roslyn diagnostics. Runtime code keeps only necessary null argument checks. |

## 1. Scope

| In Scope | Details |
|---|---|
| `Entity -> Dto` | Generate `Dto ToTarget(Entity source)`. |
| `CreateDto -> Entity` | Generate `Entity ToTarget(CreateDto source)`. |
| `UpdateDto -> Entity` | Generate `void Apply(UpdateDto source, Entity destination)`. |
| Mapping attributes | Clarify and enforce `MapFrom`, `MapName`, `MapIgnore`, and `MapConvert` semantics. |
| Diagnostics | Missing members, read-only targets, incompatible types, invalid paths, nullability hazards, and protected field writes. |
| CRUD generator integration | CRUD generator emits mapping declarations and uses generated mapping methods. |
| Entity DTO generator integration | DTO generation and mapping rule resolution must use the same property visibility rules. |
| Tests | Generator, diagnostic, CRUD integration, and sample coverage for the generated mainline. |
| Documentation cleanup | Project docs should no longer describe AutoMapper as the mapping mainline. |

| Out Of Scope | Reason |
|---|---|
| AutoMapper integration | Conflicts with AoT and compile-time generation direction. |
| Runtime expression interpretation | Adds reflection-like behavior and unclear failure timing. |
| Runtime converter registry | This feature should keep conversion static and generated. |
| Arbitrary LINQ projection generation | Query projection is separate from object mapping. |
| Complex culture-aware parsing | Needs explicit business decisions and should be opt-in through `MapConvert`. |
| Implicit navigation graph traversal | Too easy to create accidental database and nullability behavior. |
| Collection synchronization for updates | `Apply` updates scalar object state only; aggregate child synchronization is domain logic. |

## 2. Architecture

| Component | Layer | Responsibility |
|---|---|---|
| `GenerateObjectMappingAttribute` | `Domain.Shared` | Declares source type, target type, and mapping direction. |
| `MapDirection` | `Domain.Shared` | Defines `Both`, `Create`, and `Apply` generation behavior. |
| `MapFromAttribute` | `Domain.Shared` | Declares the source member or explicit source path for the current DTO property. |
| `MapNameAttribute` | `Domain.Shared` | Declares the target member name for input DTO fields when names differ. |
| `MapIgnoreAttribute` | `Domain.Shared` | Excludes a property from the current mapping direction. |
| `MapConvertAttribute` | `Domain.Shared` | Opts into explicit conversion where default static rules are insufficient. |
| `ObjectMappingSourceGenerator` | `CodeGenerator` | Finds mapping declarations and emits generated mapping source. |
| `ObjectMappingRuleResolver` | `CodeGenerator` | Resolves property pairs, protection rules, conversion rules, and diagnostics. |
| `ObjectMappingCodeWriter` | `CodeGenerator` | Writes `ToTarget`, `Apply`, and partial hook methods. |
| `CrudServiceSourceGenerator` | `CodeGenerator` | Emits CRUD DTOs, mapping declarations, and service calls to generated mapping methods. |
| `EntitySourceGenerator` | `CodeGenerator` | Uses the same property inclusion and exclusion rules when generating DTOs. |

The mainline is:

```text
Entity / DTO source
  -> GenerateObjectMapping declaration
  -> ObjectMappingRuleResolver
  -> ObjectMappingCodeWriter
  -> static ToTarget / Apply
  -> generated CRUD AppService
```

The implementation must not add a second runtime mapping path. If generated code cannot be produced safely, the generator should report a diagnostic instead of generating a best-effort mapper.

## 3. Mapping Declarations

For entity `Book`, generated CRUD should emit one partial mapping declaration class:

```csharp
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
[GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
[GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
public static partial class BookObjectMappings
{
}
```

Expected generated methods:

| Direction | Method | Used by |
|---|---|---|
| Entity to DTO | `BookDto ToTarget(Book source)` | `Create`, `GetById`, `GetList`, `Update` responses |
| Create DTO to entity | `Book ToTarget(CreateBookDto source)` | `CreateAsync` |
| Update DTO to entity | `void Apply(UpdateBookDto source, Book destination)` | `UpdateAsync` |

Multiple `GenerateObjectMapping` attributes on the same class are supported. File names and generated member names must remain stable and collision-free when a class contains multiple declarations.

## 4. Attribute Semantics

| Attribute | Mainline usage | Behavior |
|---|---|---|
| `[MapFrom("Name")]` | DTO output property | Reads source property `Name` into the current DTO property. |
| `[MapFrom("Category.Name")]` | DTO output property | Reads an explicit navigation path. Each path segment must exist. |
| `[MapName("EntityName")]` | Create or update DTO property | Writes this DTO property into target property `EntityName`. |
| `[MapIgnore]` | DTO property | Excludes this property from mapping for the current declaration. |
| `[MapConvert]` | DTO property | Allows an explicit generated conversion method or conversion rule. |

DTO-side attributes are the documented default. Entity-side attributes are not the preferred way to shape DTO contracts because that makes one domain property silently affect multiple DTOs.

When both source and target rules are present for the same member, the resolver must pick one deterministic rule and report ambiguity if the result is not clear.

## 5. Data Flow

### Entity To DTO

| Step | Rule |
|---|---|
| Source | Entity instance. |
| Target | New DTO instance. |
| Matching | Same-name public readable source property to public writable DTO property. |
| Rename | DTO property can use `[MapFrom("SourceProperty")]`. |
| Navigation path | Only explicit `[MapFrom("Navigation.Property")]`. |
| Ignore | DTO property `[MapIgnore]` is skipped. |
| Null source | Generated method throws `ArgumentNullException`. |
| Hooks | Generated method calls `AfterToTarget(source, destination)` partial hook when present. |

### Create DTO To Entity

| Step | Rule |
|---|---|
| Source | Create DTO instance. |
| Target | New entity instance. |
| Matching | Same-name DTO property to writable entity property. |
| Rename | DTO property can use `[MapName("EntityProperty")]`. |
| Protected fields | Never mapped from create DTO. |
| Tenant and audit | Set by generated CRUD/application service logic, not DTO mapping. |
| Hooks | Generated method calls `AfterToTarget(source, destination)` partial hook when present. |

### Update DTO Apply

| Step | Rule |
|---|---|
| Source | Update DTO instance. |
| Target | Existing tracked entity instance. |
| Matching | Same-name DTO property to writable entity property. |
| Rename | DTO property can use `[MapName("EntityProperty")]`. |
| Protected fields | Never overwritten by `Apply`. |
| Null source or destination | Generated method throws `ArgumentNullException`. |
| Hooks | Generated method calls `BeforeApply(source, destination)` and `AfterApply(source, destination)` partial hooks when present. |

## 6. Protected Fields

The following fields are protected for `CreateDto -> Entity` and `UpdateDto -> Entity.Apply`:

| Field | Behavior |
|---|---|
| `Id` | Never accepted from DTO input. |
| `TenantId` | Set from `CurrentUser` / `CurrentTenant` mainline, not from input. |
| `CreationTime` | Set by creation audit logic. |
| `CreatorId` | Set by creation audit logic. |
| `LastModificationTime` | Set by modification audit logic. |
| `LastModifierId` | Set by modification audit logic. |
| `DeletionTime` | Set by soft-delete logic. |
| `DeleterId` | Set by soft-delete logic. |
| `IsDeleted` | Set by soft-delete logic. |
| `ConcurrencyStamp` | Used for concurrency checks and output, but not copied into the entity by `Apply`. |

If a DTO explicitly tries to map into one of these fields, the generator should report `OM009`. The generated assignment must not be emitted.

## 7. Type Conversion

| Type relationship | Behavior |
|---|---|
| Same type | Direct assignment. |
| Implicitly assignable type | Direct assignment. |
| Non-nullable to nullable | Direct assignment. |
| Nullable to non-nullable | Diagnostic unless an explicit conversion rule exists. |
| Enum to string | `value.ToString()`. |
| String to enum | Generated `Enum.Parse<TEnum>(value)` for non-null input; nullability must be validated by diagnostics. |
| Enum to integer | Explicit cast. |
| Integer to enum | Explicit cast. |
| Numeric primitive to numeric primitive | Explicit cast when C# permits it. |
| Guid to string | `value.ToString()`. |
| String to Guid | `Guid.Parse(value)` when nullability is safe. |
| DateTime/string | No default conversion. Require `MapConvert`. |
| Complex type | Require an available generated element/object mapping or report diagnostic. |
| Collection element type | Require compatible element assignment or generated element mapping. |

Generated conversion code must be deterministic and AoT-safe. It must not resolve converter services from DI.

## 8. Diagnostics

Diagnostics are part of the public developer experience and should remain stable.

| Code | Severity | Meaning |
|---|---|---|
| `OM001` | Error | Target property is not mapped and not ignored. |
| `OM002` | Error | Source property or path segment does not exist. |
| `OM003` | Error | Mapping rule is ambiguous. |
| `OM004` | Error | Source and target types are incompatible. |
| `OM005` | Error | Nullability is unsafe. |
| `OM006` | Error | Target property is read-only. |
| `OM007` | Error | Collection element mapping is missing. |
| `OM008` | Error | Explicit navigation path is invalid. |
| `OM009` | Error | DTO input attempts to map a protected field. Assignment must be skipped. |

`OM009` is an error because explicit input mapping into protected fields is almost always a security or consistency bug. The generator must not emit the assignment.

Runtime exceptions should be limited:

| Runtime case | Behavior |
|---|---|
| `source` is null | Throw `ArgumentNullException`. |
| `destination` is null in `Apply` | Throw `ArgumentNullException`. |
| Explicit navigation path hits null | Generate null-safe access when target is nullable; otherwise emit a diagnostic requiring explicit handling. |

## 9. CRUD Integration

Generated CRUD must rely on the mapping methods:

| CRUD method | Mapping call |
|---|---|
| `CreateAsync` | `var entity = BookObjectMappings.ToTarget(input);` then `BookObjectMappings.ToTarget(created)` for response. |
| `GetByIdAsync` | `BookObjectMappings.ToTarget(entity)`. |
| `GetListAsync` | `entities.Select(BookObjectMappings.ToTarget)`. |
| `UpdateAsync` | `BookObjectMappings.Apply(input, entity)` then `BookObjectMappings.ToTarget(updated)`. |
| `DeleteAsync` | No DTO mapping. |

The CRUD generator may emit mapping declarations, but it should not generate hand-written property assignment logic inside the app service.

## 10. Entity DTO Generator Integration

The Entity DTO generator and Object Mapping resolver must agree on property inclusion:

| Property type | DTO generation | Mapping behavior |
|---|---|---|
| Scalar public property | Include unless excluded by DTO rules. | Auto-map by name. |
| Navigation reference | Exclude by default. | Map only with explicit `MapFrom` path. |
| Navigation collection | Exclude by default. | Map only with explicit mapping and element mapping. |
| Protected fields in input DTOs | Exclude. | Never assign. |
| Protected fields in output DTOs | Include only when the DTO contract needs them, such as `ConcurrencyStamp`. | Entity to DTO can map them. |

This prevents DTO generation from producing properties that the mapper cannot safely populate.

## 11. Error Handling

The generator must prefer compile-time feedback:

| Scenario | Behavior |
|---|---|
| Missing source member | Diagnostic. |
| Missing target member from `MapName` | Diagnostic. |
| Read-only target property | Diagnostic. |
| Incompatible type | Diagnostic. |
| Unsafe nullability | Diagnostic. |
| Invalid navigation path | Diagnostic. |
| Protected input field | Skip assignment and report `OM009`. |

There should be no best-effort runtime fallback. Generated code either compiles with deterministic assignments or the build tells the developer what to fix.

## 12. Testing Strategy

| Test area | Required coverage |
|---|---|
| ObjectMapping generator | `ToTarget`, `Apply`, multi-declaration classes, direction-specific output, file name collision prevention. |
| Attribute rules | `MapFrom`, `MapName`, `MapIgnore`, `MapConvert`, explicit navigation paths. |
| Protected fields | Create/update DTO mappings do not assign tenant, audit, soft-delete, identity, or concurrency fields. |
| Conversion rules | Same type, nullable, enum/string/int, numeric cast, Guid/string, unsupported DateTime/string. |
| Diagnostics | `OM001` through `OM009` cases. |
| CRUD generator | Generated CRUD emits mapping declarations and calls only generated mapping methods. |
| Entity DTO generator | DTO inclusion/exclusion matches mapping resolver expectations. |
| Compilation | Generated code compiles in test harness without AutoMapper references. |
| Sample path | Sample CRUD can create, read, update, and list without hand-written basic DTO mappings. |

Named tests from the feature plan should be included:

| Test | Purpose |
|---|---|
| `MapFrom_ShouldGenerateExpectedAssignment` | Verifies rename/path assignment. |
| `MapIgnore_ShouldSkipProperty` | Verifies ignored members have no assignment. |
| `InvalidMapFrom_ShouldEmitDiagnostic` | Verifies bad source member reports diagnostic. |
| `UpdateDtoApplyTo_ShouldNotOverwriteCreateAuditFields` | Verifies protected fields are not overwritten by `Apply`. |

Additional recommended tests:

| Test | Purpose |
|---|---|
| `MapName_ShouldMapInputDtoPropertyToEntityProperty` | Verifies input DTO rename semantics. |
| `NavigationPath_ShouldRequireExplicitMapFrom` | Verifies no implicit navigation scan. |
| `InvalidNavigationPath_ShouldEmitDiagnostic` | Verifies path segment validation. |
| `NullableToNonNullable_ShouldEmitDiagnostic` | Verifies nullability safety. |
| `ProtectedInputField_ShouldEmitOM009` | Verifies protected assignment is blocked. |
| `CrudGenerator_ShouldUseGeneratedObjectMappingsOnly` | Prevents hand-written assignment in CRUD app service. |
| `SampleCrud_ShouldWorkWithoutManualDtoMapping` | Verifies end-to-end developer experience. |

## 13. Acceptance Criteria

| Criterion | Acceptance standard |
|---|---|
| Compile-time mapping | Mapping output is generated static C# code. |
| CRUD mainline | Generated CRUD uses `ToTarget` and `Apply`; no AutoMapper or runtime mapper is needed. |
| Mapping correctness | `MapFrom`, `MapName`, `MapIgnore`, simple conversion, and explicit navigation paths work as specified. |
| Compile diagnostics | Invalid mappings produce stable Roslyn diagnostics. |
| Update safety | `Apply` does not overwrite protected fields. |
| DTO consistency | DTO generation does not expose properties that the mapper cannot safely handle. |
| AoT friendly | No runtime reflection mapping, expression interpretation, or DI converter lookup. |
| Documentation | AutoMapper is not documented as the framework mapping mainline. |

## 14. Implementation Notes

The implementation plan should stay focused:

| Area | Guidance |
|---|---|
| Existing resolver | Extend `ObjectMappingRuleResolver` rather than creating a parallel resolver. |
| Existing writer | Extend `ObjectMappingCodeWriter` rather than emitting mapping code in CRUD generator. |
| Diagnostics | Keep codes stable and testable. |
| Protected fields | Centralize field names in one helper to avoid CRUD and mapping divergence. |
| Path mapping | Keep path support explicit and shallow enough to generate readable null-safe code. |
| Documentation | Update only docs that currently point developers to AutoMapper as the mainline. |
