# Object Mapping Code Generator Design

**Date:** 2026-04-25  
**Status:** Approved  
**Parent:** AOT-friendly object mapping platform capability

## 1. Overview

Introduce a compile-time object mapping generator using Roslyn `IIncrementalGenerator`. The generator produces static mapping code for POCO-to-POCO mappings declared via `[GenerateObjectMapping]` attribute. The generated code is AOT-friendly with no runtime reflection, no `Activator`, no profile scanning.

This capability is intentionally separated from entity generation. The long-term main path is generated code, not reflection-based mapping.

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Integration | Independent attribute | Clean separation, not tied to entity generation |
| Output shape | Minimal (ToTarget, Apply, ToTargetExpression) | Simple, focused, easy to understand |
| Customization | Attributes + partial methods | Attributes for simple cases, hooks for complex logic |
| Migration | Parallel coexistence | No forced migration, gradual adoption |

## 2. Goals

- Support explicit mapping between arbitrary source and target types
- Generate static mapping APIs at compile time
- Avoid runtime reflection, `Activator`, profile scanning, and runtime fallback
- Provide deterministic diagnostics for unsupported or ambiguous mappings
- Support projection expressions for query pipelines
- Make migration away from `AutoMapper` incremental and measurable

## 3. Non-Goals

- Do not implement a general-purpose runtime object mapper
- Do not implement profile discovery or convention-based runtime registration
- Do not support complex object graphs as a primary feature
- Do not attempt deep polymorphic mapping, cyclic graph resolution, or dynamic source/target resolution
- Do not introduce a second runtime mapping path as a compatibility layer
- Do not tie mapping generation to entity generation

## 4. Architecture

```
framework/tools/CrestCreates.CodeGenerator/
├── ObjectMappingGenerator/                    # NEW
│   ├── ObjectMappingSourceGenerator.cs        # IIncrementalGenerator entry
│   ├── ObjectMappingModel.cs                  # Mapping model (source/target types, properties)
│   ├── ObjectMappingRuleResolver.cs           # Property matching and type compatibility
│   ├── ObjectMappingCodeWriter.cs             # C# code generation
│   └── ObjectMappingDiagnostics.cs            # Compile-time diagnostics
└── ...

framework/src/CrestCreates.Domain.Shared/
└── ObjectMapping/
    ├── GenerateObjectMappingAttribute.cs      # Entry point attribute
    ├── MapDirection.cs                        # MapDirection enum (Create, Apply, Both)
    ├── MapIgnoreAttribute.cs                  # Skip property
    ├── MapNameAttribute.cs                    # Rename property
    └── MapFromAttribute.cs                    # Source property override
```

The generator is independent from entity generation. It can be referenced by entity-driven workflows where that reduces friction, but it is not coupled to them.

## 5. Entry Model

### 5.1 GenerateObjectMappingAttribute

```csharp
namespace CrestCreates.Domain.Shared.ObjectMapping;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateObjectMappingAttribute : Attribute
{
    public Type SourceType { get; }
    public Type TargetType { get; }
    
    /// <summary>
    /// Mapping direction. Default is Both (ToTarget + Apply).
    /// </summary>
    public MapDirection Direction { get; set; } = MapDirection.Both;
    
    public GenerateObjectMappingAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }
}

public enum MapDirection
{
    Create = 1,    // Only ToTarget method
    Apply = 2,     // Only Apply method  
    Both = 3       // Both methods (default)
}
```

### 5.2 Usage Examples

```csharp
// Single mapping declaration - generates ToTarget, Apply, ToTargetExpression
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookMappers { }

// For multiple mappings, use separate partial classes with descriptive names
[GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
public static partial class CreateBookDtoToBookMapper { }

[GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
public static partial class UpdateBookDtoToBookMapper { }
```

**Naming Convention:** Each mapping declaration should use a dedicated partial class with a descriptive name (e.g., `CreateBookDtoToBookMapper`). This ensures clear method names and avoids conflicts.

### 5.3 Why a Dedicated Attribute

- Mapping is broader than CRUD and should not be tied to entity generation
- Explicit mapping declaration keeps the generator predictable
- A dedicated entry point makes it easier to reason about diagnostics and generated outputs
- Supports arbitrary POCO-to-POCO mappings, not just entity-DTO pairs

## 6. Generated Output

### 6.1 Output Structure

Each `[GenerateObjectMapping]` declaration generates methods in the declaring partial class. One partial class per mapping declaration.

```csharp
// Declaration: [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
// File: BookToBookDtoMapper.g.cs
public static partial class BookToBookDtoMapper
{
    /// <summary>
    /// Maps Book to BookDto (creates new instance).
    /// </summary>
    public static BookDto ToTarget(Book source)
    {
        if (source is null) 
            throw new ArgumentNullException(nameof(source));
        
        var result = new BookDto
        {
            Id = source.Id,
            Title = source.Title,
            Author = source.Author,
        };
        
        AfterToTarget(source, result);
        return result;
    }
    
    /// <summary>
    /// Applies Book values to existing BookDto.
    /// </summary>
    public static void Apply(Book source, BookDto destination)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (destination is null)
            throw new ArgumentNullException(nameof(destination));
        
        BeforeApply(source, destination);
        destination.Title = source.Title;
        destination.Author = source.Author;
    }
    
    /// <summary>
    /// Projection expression for query pipelines.
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<Book, BookDto>> ToTargetExpression =>
        source => new BookDto
        {
            Id = source.Id,
            Title = source.Title,
            Author = source.Author,
        };
    
    // Partial hooks
    partial void AfterToTarget(Book source, BookDto destination);
    partial void BeforeApply(Book source, BookDto destination);
}
```

### 6.2 Method Generation by Direction

| Direction | ToTarget | Apply | ToTargetExpression |
|-----------|----------|-------|-------------------|
| Create | ✓ | ✗ | ✓ |
| Apply | ✗ | ✓ | ✗ |
| Both | ✓ | ✓ | ✓ |

### 6.3 Partial Method Hooks

Generated partial methods for custom post-processing:

```csharp
partial void AfterToTarget(TSource source, TTarget destination);
partial void BeforeApply(TSource source, TTarget destination);
```

Users implement these in their partial class:

```csharp
public static partial class BookToBookDtoMapper
{
    partial void AfterToTarget(Book source, BookDto destination)
    {
        destination.DisplayName = $"{source.Title} by {source.Author}";
    }
}
```

## 7. Mapping Rules

### 7.1 Property Matching Priority

1. **Explicit override**: `[MapFrom("PropertyName")]` on target property
2. **Name override**: `[MapName("SourceName")]` on target property
3. **Same-name matching**: Default behavior

### 7.2 Type Compatibility Rules

| Source Type | Target Type | Allowed | Notes |
|-------------|-------------|---------|-------|
| T | T | ✓ | Direct assignment |
| T | T? | ✓ | Implicit nullable conversion |
| T? | T | ✓ | With null check (throws if null) |
| T? | T? | ✓ | Direct assignment |
| Enum A | Enum B | ✓ | If same underlying type |
| IEnumerable<T> | List<T> | ✓ | If element mapping exists |
| IEnumerable<T> | T[] | ✓ | If element mapping exists |

### 7.3 Unsupported Cases (Produce Diagnostics)

- Missing source member (error)
- Missing target member (warning)
- Type incompatibility (error)
- Read-only destination member (error for Apply, skipped for ToTarget)
- Ambiguous mapping candidates (error)
- Missing collection element mapping (error)

### 7.4 Supported Mapping Scenarios

- Entity to DTO
- DTO to entity
- Update DTO applied to existing entity
- List-item or summary DTOs
- Collection-to-collection mapping
- Expression projection for query pipelines

## 8. Customization Model

### 8.1 Customization Attributes

```csharp
namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Skip mapping for this property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapIgnoreAttribute : Attribute { }

/// <summary>
/// Map to/from a differently-named property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapNameAttribute : Attribute
{
    public string SourceName { get; }
    public MapNameAttribute(string sourceName) => SourceName = sourceName;
}

/// <summary>
/// Explicit source property for this target property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapFromAttribute : Attribute
{
    public string SourceProperty { get; }
    public MapFromAttribute(string sourceProperty) => SourceProperty = sourceProperty;
}
```

### 8.2 Usage Examples

```csharp
public class BookDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    
    [MapIgnore]
    public string? InternalNotes { get; set; }  // Not mapped
    
    [MapName("AuthorName")]
    public string? Author { get; set; }  // Maps from source.AuthorName
    
    [MapFrom(nameof(Book.ISBN))]
    public string? Isbn { get; set; }  // Maps from source.ISBN
}
```

### 8.3 Partial Method Hooks

For complex customization that cannot be expressed with attributes:

```csharp
public static partial class BookToBookDtoMapper
{
    partial void AfterToTarget(Book source, BookDto destination)
    {
        // Computed property
        destination.DisplayName = $"{source.Title} by {source.AuthorName}";
        
        // Conditional mapping
        if (source.IsSpecialEdition)
        {
            destination.Tags = new[] { "Special Edition" };
        }
    }
}

public static partial class UpdateBookDtoToBookMapper
{
    partial void BeforeApply(UpdateBookDto source, Book destination)
    {
        // Audit logic before applying
        destination.LastModified = DateTime.UtcNow;
    }
}
```

## 9. AOT Constraints

The generator must produce normal C# code that is safe for NativeAOT-oriented builds.

### 9.1 Required Constraints

- No reflection-based member access
- No `Activator.CreateInstance`
- No runtime compiled expressions
- No mapper profile scanning
- No runtime fallback path
- No hidden dependency on `AutoMapper`

### 9.2 Expression Tree Handling

The generated projection expression is exposed as `Expression<Func<...>>`, but it must remain a static expression tree and must not be compiled at runtime.

```csharp
// This is safe - static expression tree
public static Expression<Func<Book, BookDto>> ToTargetExpression =>
    source => new BookDto { ... };

// This is NOT allowed - runtime compilation
var compiled = ToTargetExpression.Compile(); // Must not be generated
```

## 10. Diagnostics

### 10.1 Diagnostic Codes

| Code | Severity | Title | Message |
|------|----------|-------|---------|
| OM001 | Error | Source type not found | Source type '{0}' could not be found |
| OM002 | Error | Target type not found | Target type '{0}' could not be found |
| OM003 | Error | Source property not found | Source property '{0}' not found on type '{1}' |
| OM004 | Warning | Target property not mapped | Target property '{0}' on type '{1}' has no matching source |
| OM005 | Error | Type incompatibility | Cannot map property '{0}': type '{1}' is not compatible with '{2}' |
| OM006 | Error | Read-only target | Target property '{0}' is read-only and cannot be mapped in Apply direction |
| OM007 | Error | Ambiguous mapping | Multiple source properties match target '{0}': {1} |
| OM008 | Error | Missing element mapping | Cannot map collection '{0}': no mapping exists for element type '{1}' to '{2}' |
| OM009 | Error | Nullability mismatch | Source property '{0}' is nullable but target is non-nullable without null check |

### 10.2 Failure Mode

The preferred failure mode is compile-time error, not silent runtime skipping. All diagnostics must be deterministic and reproducible.

## 11. Integration With Existing Code

### 11.1 Current State

Current code generation and application services use `AutoMapper`:
- `CrudServiceSourceGenerator` generates `{Entity}MappingProfile` classes
- `CrudServiceBase` depends on `IMapper`
- Generated CRUD services inject `IMapper`

### 11.2 Migration Path

Generated mappers coexist with AutoMapper profiles:

1. **Phase 1 - Parallel**: Both generated mappers and AutoMapper profiles exist
2. **Phase 2 - Opt-in**: Services can switch to generated mappers by calling static methods directly
3. **Phase 3 - Remove AutoMapper**: Once all services migrated, remove AutoMapper dependency

### 11.3 Example Migration

Before (AutoMapper):
```csharp
public class BookCrudService : IBookCrudService
{
    private readonly IMapper _mapper;
    
    public async Task<BookDto> CreateAsync(CreateBookDto input)
    {
        var entity = _mapper.Map<Book>(input);
        // ...
        return _mapper.Map<BookDto>(entity);
    }
}
```

After (Generated Mapper):
```csharp
public class BookCrudService : IBookCrudService
{
    public async Task<BookDto> CreateAsync(CreateBookDto input)
    {
        var entity = CreateBookDtoToBookMapper.ToTarget(input);  // Generated
        // ...
        return BookToBookDtoMapper.ToTarget(entity);  // Generated
    }
}
```

## 12. Testing Strategy

### 12.1 Generator Snapshot Tests

Verify generated `.g.cs` output matches expected shape and naming:
- Basic entity-to-DTO mapping
- DTO-to-entity mapping with Apply direction
- Properties with customization attributes
- Collection properties
- Nullable properties

### 12.2 Compilation Tests

Compile generated code in a real test project to catch:
- Syntax errors
- Missing references
- Type mismatches

### 12.3 Behavior Tests

Cover runtime behavior:
- `ToTarget` creates correct instance with all mapped properties
- `Apply` updates existing instance correctly
- Nullable handling (null throws, nullable-to-non-nullable)
- Collection mapping (List, Array, IEnumerable)
- Projection expression usage in LINQ queries
- Partial method hooks are called

### 12.4 AOT-Oriented Checks

Where feasible, verify generated code:
- Contains no `System.Reflection` usage
- Contains no `Activator.CreateInstance`
- Contains no runtime expression compilation
- Expression trees are static only

## 13. Acceptance Criteria

- [ ] The framework can generate static object mapping code for arbitrary POCO pairs
- [ ] Generated mapping does not require runtime reflection or profile scanning
- [ ] Diagnostics catch unsupported mappings during compilation
- [ ] At least one sample path can migrate away from AutoMapper
- [ ] The generator remains aligned with the framework's AOT-first main path
- [ ] Generated code compiles without errors in test projects
- [ ] Behavior tests pass for all supported mapping scenarios
