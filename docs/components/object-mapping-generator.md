# Object Mapping Generator

## Overview

The Object Mapping Generator is a compile-time source generator that produces AOT-friendly static mapping code for POCO-to-POCO mappings.

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
- **Explicit Declaration**: Each mapping is explicitly declared
- **Compile-Time Diagnostics**: Errors reported at build time
- **Projection Expressions**: Support for LINQ query pipelines
- **Customization**: Attributes and partial method hooks

## Attributes

| Attribute | Purpose |
|-----------|---------|
| `[GenerateObjectMapping]` | Declare a mapping |
| `[MapIgnore]` | Skip property |
| `[MapName]` | Rename source property |
| `[MapFrom]` | Explicit source property |

## Direction

```csharp
public enum MapDirection
{
    Create = 1,  // Only ToTarget
    Apply = 2,   // Only Apply
    Both = 3     // Both methods (default)
}
```

## Migration from AutoMapper

Generated mappers coexist with AutoMapper. Services can gradually migrate by calling static mapper methods directly.
