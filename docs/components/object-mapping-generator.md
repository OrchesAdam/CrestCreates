# Object Mapping Generator

## Overview

The Object Mapping Generator is a compile-time source generator that produces AOT-friendly static mapping code for POCO-to-POCO mappings. It generates type-safe mapping methods without runtime reflection, making it ideal for performance-critical applications and AoT compilation scenarios.

## Quick Start

1. Add the attribute to a static partial class:

```csharp
using CrestCreates.Domain.Shared.ObjectMapping;

[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookToBookDtoMapper { }
```

2. The generator produces:

```csharp
public static partial class BookToBookDtoMapper
{
    public static BookDto ToTarget(Book source) { ... }
    public static void Apply(Book source, BookDto destination) { ... }
    public static Expression<Func<Book, BookDto>> ToTargetExpression { get; }
}
```

## Features

- **AOT-Friendly**: No runtime reflection, no `Activator.CreateInstance`
- **Explicit Declaration**: Each mapping is explicitly declared via attributes
- **Compile-Time Diagnostics**: Errors reported at build time with detailed messages
- **Projection Expressions**: Support for LINQ query pipelines via `ToTargetExpression`
- **Customization**: Rich attribute system and partial method hooks
- **Inheritance Support**: Automatically maps properties from base classes
- **Collection Mapping**: Automatic conversion between `IEnumerable<T>`, `List<T>`, and arrays
- **Enum Mapping**: Compatible enums with same underlying type map directly
- **Null Safety**: Handles nullable-to-non-nullable conversions with default value fallback

## Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[GenerateObjectMapping]` | Class | Declares a mapping between two types |
| `[MapIgnore]` | Property | Excludes property from mapping |
| `[MapName("sourceName")]` | Property | Maps to a differently-named source property |
| `[MapFrom(nameof(Source.Prop))]` | Property | Explicitly specifies source property with compile-time safety |

### GenerateObjectMappingAttribute

```csharp
[GenerateObjectMapping(typeof(SourceType), typeof(TargetType), Direction = MapDirection.Both)]
public static partial class Mapper { }
```

Parameters:
- `SourceType` (required): The type to map from
- `TargetType` (required): The type to map to
- `Direction` (optional): Controls which methods are generated

## Direction Control

The `MapDirection` enum controls which mapping methods are generated:

```csharp
public enum MapDirection
{
    Create = 1,  // Only ToTarget method
    Apply = 2,   // Only Apply method
    Both = 3     // Both methods (default)
}
```

### When to use each direction:

| Direction | Use Case |
|-----------|----------|
| `Create` | DTO to entity creation scenarios; returns new instance |
| `Apply` | Update scenarios; modifies existing instance |
| `Both` | Full mapping support (default) |

### Example: Create-only mapping

```csharp
[GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
public static partial class CreateBookMapper { }

// Generates only:
// public static Book ToTarget(CreateBookDto source)
```

### Example: Apply-only mapping

```csharp
[GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
public static partial class UpdateBookMapper { }

// Generates only:
// public static void Apply(UpdateBookDto source, Book destination)
```

## Usage Examples

### Basic Mapping

Properties with matching names are mapped automatically:

```csharp
public class Book
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Author { get; set; }
}

public class BookDto
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Author { get; set; }
}

[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookMapper { }

// Usage:
var dto = BookMapper.ToTarget(book);
BookMapper.Apply(book, existingDto);
```

### Property Customization

#### MapIgnore - Skip a property

```csharp
public class User
{
    public string Name { get; set; }
    public string PasswordHash { get; set; }
}

public class UserDto
{
    public string Name { get; set; }

    [MapIgnore]
    public string PasswordHash { get; set; } // Not mapped
}
```

#### MapName - Rename source property

```csharp
public class Source
{
    public string FullName { get; set; }
}

public class Target
{
    [MapName("FullName")]
    public string Name { get; set; }
}

// Generates: Name = source.FullName
```

#### MapFrom - Explicit source with compile-time safety

```csharp
public class Source
{
    public string Title { get; set; }
}

public class Target
{
    [MapFrom(nameof(Source.Title))]
    public string Name { get; set; }
}

// Generates: Name = source.Title
```

### Null Handling

#### Nullable to non-nullable (value types)

```csharp
public class Source
{
    public int? Count { get; set; }
}

public class Target
{
    public int Count { get; set; }
}

// Generates: Count = source.Count ?? 0
```

#### Nullable to non-nullable (reference types)

```csharp
public class Source
{
    public string? Name { get; set; }
}

public class Target
{
    public string Name { get; set; }
}

// Generates: Name = source.Name ?? string.Empty
```

#### Non-nullable to nullable

```csharp
public class Source
{
    public int Count { get; set; }
}

public class Target
{
    public int? Count { get; set; }
}

// Generates: Count = source.Count (direct assignment)
```

### Collection Mapping

#### Same collection types

```csharp
public class Source
{
    public List<string> Tags { get; set; }
    public int[] Numbers { get; set; }
}

public class Target
{
    public List<string> Tags { get; set; }
    public int[] Numbers { get; set; }
}

// Generates direct assignment: Tags = source.Tags, Numbers = source.Numbers
```

#### IEnumerable to List

```csharp
public class Source
{
    public IEnumerable<int> Numbers { get; set; }
}

public class Target
{
    public List<int> Numbers { get; set; }
}

// Generates: Numbers = source.Numbers.ToList()
```

#### IEnumerable to Array

```csharp
public class Source
{
    public IEnumerable<int> Numbers { get; set; }
}

public class Target
{
    public int[] Numbers { get; set; }
}

// Generates: Numbers = source.Numbers.ToArray()
```

### Enum Mapping

Enums with the same underlying type are compatible:

```csharp
public enum Status { Active, Inactive }
public enum StatusDto { Active, Inactive }

public class Source
{
    public Status Status { get; set; }
}

public class Target
{
    public StatusDto Status { get; set; }
}

// Generates: Status = source.Status (direct cast)
```

### Partial Method Hooks

Customize mapping behavior with partial methods:

```csharp
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookMapper
{
    // Called after ToTarget creates the result
    static partial void AfterToTarget(Book source, BookDto destination)
    {
        destination.DisplayName = $"{source.Title} by {source.Author}";
    }

    // Called before Apply modifies the destination
    static partial void BeforeApply(Book source, BookDto destination)
    {
        // Custom logic before mapping
    }

    // Called after Apply modifies the destination
    static partial void AfterApply(Book source, BookDto destination)
    {
        destination.LastModified = DateTime.UtcNow;
    }
}
```

Available hooks:
- `AfterToTarget(Source, Target)` - Called after `ToTarget` creates new instance
- `BeforeApply(Source, Target)` - Called before `Apply` modifies destination
- `AfterApply(Source, Target)` - Called after `Apply` modifies destination

### Inherited Properties

The generator automatically includes properties from base classes:

```csharp
public class BaseEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Book : BaseEntity
{
    public string Title { get; set; }
}

public class BookDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Title { get; set; }
}

[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookMapper { }

// All properties (including Id, CreatedAt from BaseEntity) are mapped
```

## Migration from AutoMapper

The Object Mapping Generator can coexist with AutoMapper, allowing gradual migration.

### Step 1: Add mapper declarations

Create static mapper classes alongside existing AutoMapper profiles:

```csharp
// Existing AutoMapper profile
public class BookProfile : Profile
{
    public BookProfile()
    {
        CreateMap<Book, BookDto>();
    }
}

// New static mapper
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookMapper { }
```

### Step 2: Update call sites gradually

Replace AutoMapper calls with static mapper calls:

```csharp
// Before (AutoMapper)
var dto = _mapper.Map<BookDto>(book);

// After (generated mapper)
var dto = BookMapper.ToTarget(book);
```

### Step 3: Remove AutoMapper dependencies

Once all mappings are migrated, remove AutoMapper packages and profiles.

### Key differences from AutoMapper

| Feature | AutoMapper | Object Mapping Generator |
|---------|------------|--------------------------|
| Resolution | Runtime reflection | Compile-time generation |
| AoT compatible | No | Yes |
| DI required | Yes | No |
| Projection support | Yes | Yes (via ToTargetExpression) |
| Custom resolvers | Yes | Partial method hooks |
| Convention-based | Yes | Explicit attributes |

## Limitations

### Unsupported Features

- **Nested object mapping**: Does not automatically map nested complex types. Define separate mappers and use partial method hooks.
- **Flattening/Unflattening**: No automatic `Address.City` to `City` flattening. Use `[MapFrom]` or partial methods.
- **Custom type converters**: Use partial method hooks for custom conversion logic.
- **Runtime configuration**: All mappings are defined at compile time.

### AOT Compatibility

The generated code is fully AOT-compatible:
- No runtime reflection
- No `Activator.CreateInstance`
- No dynamic code generation
- All types are statically resolved

### Performance Considerations

- Generated code has zero runtime overhead
- No startup cost from reflection-based initialization
- Projection expressions can be translated to SQL by ORMs

## Diagnostic Reference

| Code | Severity | Title | Description | Solution |
|------|----------|-------|-------------|----------|
| OM001 | Error | Source type not found | The source type specified in `[GenerateObjectMapping]` could not be found | Check type name and namespace |
| OM002 | Error | Target type not found | The target type specified in `[GenerateObjectMapping]` could not be found | Check type name and namespace |
| OM003 | Error | Source property not found | The property specified in `[MapFrom]` or `[MapName]` does not exist on the source type | Check property name matches source type |
| OM004 | Warning | Target property not mapped | Target property has no matching source property | Add source property or use `[MapIgnore]` |
| OM005 | Error | Type incompatibility | Source property type cannot be converted to target property type | Ensure types are compatible or add custom logic |
| OM006 | Error | Read-only target | Target property is read-only and `Apply` method was requested | Use `Direction = MapDirection.Create` or remove property from mapping |
| OM007 | Error | Ambiguous mapping | Multiple source properties match the target property name | Use `[MapFrom]` to specify the exact source property |
| OM008 | Error | Collection element incompatible | Collection element types are incompatible | Ensure element types match or are convertible |
| OM009 | Warning | Nullability mismatch | Source is nullable but target is non-nullable | Default value fallback will be used |
