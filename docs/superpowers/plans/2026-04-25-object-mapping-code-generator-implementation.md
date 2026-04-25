# Object Mapping Code Generator Implementation Plan

> For agentic workers: implement this plan in order. Prefer the generated-code path over any runtime fallback. Do not introduce `AutoMapper`-style profile scanning.

**Goal:** Add a compile-time object mapping generator that emits static mapping APIs and projection expressions for arbitrary POCO-to-POCO mappings, then migrate the framework's generated CRUD path to use it.

**Architecture:** Explicit mapping declarations drive generation. The generator resolves member pairs conservatively, emits static mapper classes, and reports compile-time diagnostics for unsupported cases. Optional projection expressions support query pipelines without runtime mapping.

## Phase 1: Define Public Surface

- [ ] Add `GenerateObjectMappingAttribute` to `framework/src/CrestCreates.Domain.Shared/Attributes`
- [ ] Add `MapIgnoreAttribute`, `MapNameAttribute`, and `MapFromAttribute` only if the first pass needs them
- [ ] Add a small `MapDirection` enum if directional control is required
- [ ] Use a dedicated partial marker class per mapping pair as the declaration unit

## Phase 2: Build the Generator Skeleton

- [ ] Create `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator`
- [ ] Implement syntax/semantic discovery for mapping declarations
- [ ] Build a unified mapping model for source type, target type, members, and direction
- [ ] Add a code writer that emits static partial classes
- [ ] Add basic diagnostics plumbing

## Phase 3: Implement Member Resolution

- [ ] Resolve explicit mapping annotations first
- [ ] Resolve same-name compatible members next
- [ ] Add nullable compatibility checks
- [ ] Add collection element mapping checks
- [ ] Add simple primitive and enum conversion checks only where unambiguous
- [ ] Report diagnostics for unsupported or ambiguous matches

## Phase 4: Generate Core Mapping APIs

- [ ] Generate `ToTarget` for source-to-target creation
- [ ] Generate `Apply` for source-to-existing-target updates
- [ ] Generate optional `ToTargetExpression` for query projection
- [ ] Generate collection helpers only when they are needed by the consuming code
- [ ] Add partial hooks for post-map customization

## Phase 5: Integrate Diagnostics

- [ ] Emit missing member diagnostics
- [ ] Emit read-only destination diagnostics
- [ ] Emit type incompatibility diagnostics
- [ ] Emit collection element diagnostics
- [ ] Emit ambiguity diagnostics
- [ ] Keep diagnostics deterministic and stable for snapshot testing

## Phase 6: Replace AutoMapper Usage in Generated CRUD

- [ ] Update `CrudServiceSourceGenerator` to call generated static mapper methods
- [ ] Remove `IMapper` from generated CRUD service signatures where the generated mapper exists
- [ ] Keep transition logic narrow and temporary only if an existing generated mapper is unavailable
- [ ] Ensure the generated CRUD path does not become dependent on AutoMapper profiles

## Phase 7: Migrate One Sample Path

- [ ] Pick one LibraryManagement entity/DTO pair as the first mapping target
- [ ] Generate static mapping code for that pair
- [ ] Replace the corresponding AutoMapper profile usage
- [ ] Update the sample service to consume the generated mapper

## Phase 8: Testing

- [ ] Add snapshot tests for generated mapper files
- [ ] Add compilation tests for generated code
- [ ] Add behavior tests for source-to-target mapping
- [ ] Add behavior tests for target application mapping
- [ ] Add collection mapping tests
- [ ] Add projection expression tests

## Phase 9: Documentation and Cleanup

- [ ] Document the mapping generator entry attribute
- [ ] Document the generated file shape and naming conventions
- [ ] Document the migration path away from AutoMapper
- [ ] Update any code generation docs that still describe AutoMapper as the preferred mapping path

## Implementation Notes

- Prefer explicitness over convention
- Prefer compile-time failure over runtime fallback
- Keep the first version conservative enough to stay maintainable
- Do not grow this into a general-purpose runtime mapper
