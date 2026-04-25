# Object Mapping Code Generator Design

**Date:** 2026-04-25  
**Status:** Draft  
**Parent:** AOT-friendly object mapping platform capability

## 1. Overview

Introduce a compile-time object mapping generator for CrestCreates that supports arbitrary POCO-to-POCO mappings without runtime reflection, mapper profiles, or runtime fallback paths. The generator produces static mapping code and optional projection expressions so the framework can replace `AutoMapper`-based paths with AOT-friendly generated code.

This capability is intentionally separated from runtime mapping frameworks. The long-term main path is generated code, not reflection-based mapping.

## 2. Goals

- Support explicit mapping between arbitrary source and target types
- Generate static mapping APIs at compile time
- Avoid runtime reflection, `Activator`, profile scanning, and runtime fallback
- Provide deterministic diagnostics for unsupported or ambiguous mappings
- Support simple projection expressions for query pipelines
- Make migration away from `AutoMapper` incremental and measurable

## 3. Non-Goals

- Do not implement a general-purpose runtime object mapper
- Do not implement profile discovery or convention-based runtime registration
- Do not support complex object graphs as a primary feature
- Do not attempt deep polymorphic mapping, cyclic graph resolution, or dynamic source/target resolution
- Do not introduce a second runtime mapping path as a compatibility layer

## 4. Architecture

```text
framework/tools/CrestCreates.CodeGenerator/
├── ObjectMappingGenerator/                    # NEW: compile-time mapping generator
│   ├── ObjectMappingSourceGenerator.cs
│   ├── ObjectMappingModel.cs
│   ├── ObjectMappingRuleResolver.cs
│   ├── ObjectMappingCodeWriter.cs
│   └── ObjectMappingDiagnostics.cs
└── ...

framework/src/CrestCreates.Domain.Shared/
└── Attributes/
    ├── GenerateObjectMappingAttribute.cs      # NEW: explicit mapping declaration
    ├── MapIgnoreAttribute.cs                  # NEW: optional opt-out
    ├── MapNameAttribute.cs                   # NEW: optional name override
    └── MapFromAttribute.cs                   # NEW: optional source override
```

The generator is independent from entity generation, but it can be referenced by entity-driven workflows where that reduces friction.

## 5. Entry Model

The primary entry point is an explicit mapping declaration:

```csharp
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public partial class BookToBookDtoMapping { }

[GenerateObjectMapping(typeof(CreateBookDto), typeof(Book))]
public partial class CreateBookDtoToBookMapping { }

[GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
public partial class UpdateBookDtoToBookMapping { }
```

Why a dedicated attribute:

- Mapping is broader than CRUD and should not be tied to entity generation
- Explicit mapping declaration keeps the generator predictable
- A dedicated entry point makes it easier to reason about diagnostics and generated outputs

## 6. Generated Output

For each mapping declaration, generate one static partial mapper class:

```csharp
public static partial class BookToBookDtoMapper
{
    public static BookDto ToTarget(Book source);
    public static void Apply(BookDto source, Book destination);
    public static System.Linq.Expressions.Expression<Func<Book, BookDto>> ToTargetExpression { get; }
}
```

Generated helpers may also include:

- Collection mapping helpers
- Nullable-safe variants
- Partial hooks for custom post-processing

## 7. Mapping Rules

Mapping resolution is intentionally conservative.

### 7.1 Default Rules

1. Explicit configuration has the highest priority
2. Same-name property mapping is allowed when types are compatible
3. Nullable-compatible conversions are allowed when safe
4. Collection mapping is allowed when element mapping exists
5. Simple enum and primitive conversions may be allowed when unambiguous
6. Anything else must produce a diagnostic

### 7.2 Supported Cases

- Entity to DTO
- DTO to entity
- Update DTO applied to an existing entity
- List-item or summary DTOs
- Collection-to-collection mapping
- Expression projection for query pipelines

### 7.3 Unsupported Cases

- Implicit deep graph reconstruction
- Runtime type discovery
- Hidden member access
- Reflection-based custom resolvers
- String-based dynamic mapping rules

## 8. Customization Model

Support a small set of compile-time attributes:

```csharp
[MapIgnore]
[MapName("OtherName")]
[MapFrom(nameof(SourceProperty))]
```

If additional customization is required, prefer a small explicit mapping method over a more complex DSL.

Partial hooks are allowed for generated classes:

```csharp
partial void AfterMap(Book source, BookDto destination);
partial void BeforeApply(UpdateBookDto source, Book destination);
```

Hooks are for narrow, predictable adjustments only.

## 9. AOT Constraints

The generator must produce normal C# code that is safe for NativeAOT-oriented builds.

Required constraints:

- No reflection-based member access
- No `Activator.CreateInstance`
- No runtime compiled expressions
- No mapper profile scanning
- No runtime fallback path
- No hidden dependency on `AutoMapper`

The generated projection expression may be exposed as `Expression<Func<...>>`, but it must remain a static expression tree and must not be compiled at runtime.

## 10. Diagnostics

The generator must report deterministic compile-time diagnostics for:

- Missing target members
- Missing source members
- Type incompatibility
- Read-only destination members
- Ambiguous mapping candidates
- Missing collection element mappings
- Unsupported complex shapes

The preferred failure mode is compile-time error, not silent runtime skipping.

## 11. Integration With Existing Code

Current code generation and application services still use `AutoMapper` in several places. This generator should become the generated-code replacement path.

Relevant existing locations:

- `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`
- `framework/src/CrestCreates.Application/Services/CrudServiceBase.cs`
- `samples/LibraryManagement/LibraryManagement.Application/LibraryManagementAutoMapperProfile.cs`

The migration target is:

- CRUD services call generated static mapper methods
- Samples no longer need AutoMapper profiles for standard mappings
- Generated projection expressions can be used in query pipelines where applicable

## 12. Testing Strategy

### 12.1 Generator Snapshot Tests

Verify generated `.g.cs` output matches expected shape and naming.

### 12.2 Compilation Tests

Compile generated code in a real test project to catch syntax or reference issues.

### 12.3 Behavior Tests

Cover:

- source to target mapping
- target application mapping
- nullable handling
- collection mapping
- projection expression usage in query pipelines

### 12.4 AOT-Oriented Checks

Where feasible, verify generated code does not introduce reflection-based dependencies.

## 13. Acceptance Criteria

- The framework can generate static object mapping code for arbitrary POCO pairs
- Generated mapping does not require runtime reflection or profile scanning
- Diagnostics catch unsupported mappings during compilation
- At least one sample path can migrate away from AutoMapper
- The generator remains aligned with the framework's AOT-first main path
