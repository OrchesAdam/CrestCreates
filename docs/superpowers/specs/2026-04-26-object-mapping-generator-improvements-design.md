# Object Mapping Generator Improvements Design

**Date:** 2026-04-26
**Status:** Approved
**Parent:** Object Mapping Code Generator (2026-04-25)

## 1. Overview

This design addresses improvements to the Object Mapping Generator based on code review findings. The improvements cover important issues, minor issues, testing gaps, and documentation expansion.

### Scope Summary

| Category | Improvement | Impact Files |
|----------|-------------|--------------|
| Important | Collection element mapping check | `ObjectMappingRuleResolver.cs` |
| Important | Enum mapping check | `ObjectMappingRuleResolver.cs` |
| Important | Ambiguous mapping detection (OM007) | `ObjectMappingRuleResolver.cs` |
| Important | Expression tree null handling | `ObjectMappingCodeWriter.cs` |
| Minor | `#nullable enable` directive | `ObjectMappingCodeWriter.cs` |
| Minor | Performance optimization (compilation cache) | `ObjectMappingRuleResolver.cs` |
| Minor | Inherited property support | `ObjectMappingRuleResolver.cs` |
| Testing | Diagnostic scenario tests | Test files |
| Documentation | Examples, migration guide, limitations | Documentation file |

## 2. Collection Element Mapping

### 2.1 Implementation Location

`ObjectMappingRuleResolver.cs`, `IsTypeCompatible` method

### 2.2 Implementation Logic

```
1. Detect if source type implements IEnumerable<TSource>
2. Detect if target type is List<TTarget>, TTarget[], or IEnumerable<TTarget>
3. Extract element types TSource and TTarget
4. Validate element type compatibility:
   - Same type: OK
   - Implicit conversion exists: OK
   - Incompatible: Report OM008 diagnostic
```

### 2.3 Supported Collection Mappings

| Source Type | Target Type | Handling |
|-------------|-------------|----------|
| `IEnumerable<T>` | `List<T>` | Direct assignment (`.ToList()`) |
| `IEnumerable<T>` | `T[]` | Direct assignment (`.ToArray()`) |
| `List<T>` | `IEnumerable<T>` | Direct assignment |
| `T[]` | `IEnumerable<T>` | Direct assignment |
| `List<T>` | `List<T>` | Direct assignment |

### 2.4 Code Generation Adjustment

For collection types requiring conversion, generate `.ToList()` or `.ToArray()` calls.

### 2.5 Conservative Strategy

Only validate element type same or implicit conversion exists. Do not support nested object mapping in this version.

## 3. Enum Mapping

### 3.1 Implementation Location

`ObjectMappingRuleResolver.cs`, `IsTypeCompatible` method

### 3.2 Implementation Logic

```
1. Detect if both source and target types are enums
2. Compare underlying types (UnderlyingType):
   - Same underlying type: OK (e.g., both int)
   - Different underlying type: Report OM005 type incompatibility
```

### 3.3 Examples

```csharp
// Compatible
enum Status { Active, Inactive }     // Underlying: int
enum StatusDto { Active, Inactive }  // Underlying: int

// Incompatible
enum Priority : byte { Low, High }   // Underlying: byte
enum PriorityDto : int { Low, High } // Underlying: int -> Error
```

## 4. Ambiguous Mapping Detection (OM007)

### 4.1 Ambiguity Definition

Ambiguity occurs when multiple source properties could map to the same target property.

### 4.2 Ambiguity Scenarios

| Scenario | Example | Description |
|----------|---------|-------------|
| MapName + same-name conflict | Target `Name` has `[MapName("FullName")]`, source has both `Name` and `FullName` | Two source properties match |
| MapFrom + same-name conflict | Target `Name` has `[MapFrom("Title")]`, source has both `Name` and `Title` | Two source properties match |

### 4.3 Implementation Logic

**Location:** `ObjectMappingRuleResolver.cs`, `ResolvePropertyMapping` method

```
1. Collect all possible source property matches:
   - Explicit MapFrom -> add specified property
   - Explicit MapName -> add specified property
   - Same-name match -> add same-name property

2. If match count > 1:
   - Report OM007 diagnostic, list all matching source property names
   - Mark mapping as ignored

3. If match count == 1:
   - Use that match

4. If match count == 0:
   - Return null (unmapped)
```

### 4.4 Priority Rule

When explicit mapping is specified, do not check for ambiguity:

| Attribute | Behavior |
|-----------|----------|
| `[MapFrom("X")]` | Use only X, do not check same-name property |
| `[MapName("X")]` | Use only X, do not check same-name property |
| No attribute | Check same-name match |

## 5. Expression Tree Null Handling

### 5.1 Problem Analysis

Current `ToTarget` and `ToTargetExpression` handle nullable-to-non-nullable conversion inconsistently:

| Method | Current Behavior | Expected Behavior |
|--------|------------------|-------------------|
| `ToTarget` | `Count = source.Count ?? 0` ✓ | Keep |
| `ToTargetExpression` | `Count = source.Count` ✗ | `Count = source.Count ?? 0` |

### 5.2 Implementation

**Location:** `ObjectMappingCodeWriter.cs`, `WriteToTargetExpression` method

Reuse `GetPropertyAssignmentExpression` method which already handles `NeedsNullCheck`:

```csharp
// Current code (line 202)
sb.AppendLine($"    {mapping.TargetProperty.Name} = source.{mapping.SourceProperty.Name}{comma}");

// Modified to
var valueExpression = GetPropertyAssignmentExpression(mapping);
sb.AppendLine($"    {mapping.TargetProperty.Name} = {valueExpression}{comma}");
```

### 5.3 Expression Tree Limitation Note

Expression trees support `??` operator, so no special handling needed. Document that `ToTargetExpression` behaves consistently with `ToTarget`.

## 6. Minor Improvements

### 6.1 `#nullable enable` Directive

**Location:** `ObjectMappingCodeWriter.cs` line 15

```csharp
// Current
sb.AppendLine("// <auto-generated />");

// Modified
sb.AppendLine("// <auto-generated />");
sb.AppendLine("#nullable enable");
```

### 6.2 Performance Optimization

**Location:** `ObjectMappingRuleResolver.cs` lines 213-217

**Current Issue:** Creating temporary `CSharpCompilation` for each type check.

**Solution:** Use symbol comparison for simple cases, only use `ClassifyConversion` for complex cases.

### 6.3 Inherited Property Support

**Location:** `ObjectMappingRuleResolver.cs`, `GetProperties` method

```csharp
private List<IPropertySymbol> GetProperties(INamedTypeSymbol type)
{
    var properties = new List<IPropertySymbol>();
    var currentType = type;

    // Traverse inheritance hierarchy
    while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
    {
        foreach (var member in currentType.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.DeclaredAccessibility == Accessibility.Public &&
                !member.IsStatic &&
                member.CanBeReferencedByName &&
                !properties.Any(p => p.Name == member.Name)) // Avoid duplicates
            {
                properties.Add(member);
            }
        }
        currentType = currentType.BaseType;
    }

    return properties;
}
```

## 7. Testing Improvements

### 7.1 Diagnostic Scenario Tests

| Diagnostic Code | Test Scenario | Verification |
|-----------------|---------------|--------------|
| OM001 | Source type not found | Confirm error diagnostic generated |
| OM002 | Target type not found | Confirm error diagnostic generated |
| OM003 | MapFrom source property not found | Confirm error diagnostic generated |
| OM005 | Type incompatibility (e.g., int to string) | Confirm error diagnostic generated |
| OM006 | Read-only target property in Apply direction | Confirm error diagnostic generated |
| OM007 | Ambiguous mapping (multiple sources match target) | Confirm error diagnostic generated |
| OM008 | Collection element type incompatible | Confirm error diagnostic generated |

### 7.2 Collection Mapping Tests

| Scenario | Source Type | Target Type | Expected Result |
|----------|-------------|-------------|-----------------|
| Same collection type | `List<int>` | `List<int>` | Direct assignment |
| IEnumerable to List | `IEnumerable<int>` | `List<int>` | `.ToList()` |
| IEnumerable to Array | `IEnumerable<int>` | `int[]` | `.ToArray()` |
| Element type incompatible | `List<string>` | `List<int>` | OM008 error |

### 7.3 Enum Mapping Tests

| Scenario | Source Enum | Target Enum | Expected Result |
|----------|-------------|-------------|-----------------|
| Same underlying type | `Status : int` | `StatusDto : int` | Direct conversion |
| Different underlying type | `Priority : byte` | `PriorityDto : int` | OM005 error |

### 7.4 Inherited Property Tests

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
    public string Title { get; set; }
}

// Mapping should include inherited Id property
```

## 8. Documentation Expansion

### 8.1 Document Structure

**Location:** `docs/components/object-mapping-generator.md`

**Expanded Sections:**

1. **Usage Examples**
   - Basic mapping (entity to DTO)
   - Direction control (Create, Apply, Both)
   - Property customization (MapIgnore, MapName, MapFrom)
   - Null handling (nullable value types, nullable reference types)
   - Collection mapping (List, Array, IEnumerable)
   - Enum mapping
   - Partial method hooks (AfterToTarget, BeforeApply, AfterApply)

2. **Migration from AutoMapper**
   - Migration steps
   - Code comparison (before vs after)

3. **Limitations and Constraints**
   - Unsupported features (nested object mapping, runtime polymorphic mapping, circular reference resolution)
   - Inherited property support
   - AOT compatibility

4. **Diagnostic Reference**
   - OM001-OM009 with descriptions and solutions

### 8.2 Diagnostic Reference Table

| Code | Severity | Description | Solution |
|------|----------|-------------|----------|
| OM001 | Error | Source type not found | Check type name and namespace |
| OM002 | Error | Target type not found | Check type name and namespace |
| OM003 | Error | Source property not found | Check MapFrom/MapName specified property name |
| OM004 | Warning | Target property not mapped | Consider MapIgnore or add source property |
| OM005 | Error | Type incompatibility | Check property type compatibility |
| OM006 | Error | Read-only target property | Use in ToTarget only, or remove Apply direction |
| OM007 | Error | Ambiguous mapping | Use MapFrom to specify source property |
| OM008 | Error | Collection element type incompatible | Ensure element types compatible or add mapping declaration |
| OM009 | Warning | Nullability mismatch | Will use default value |

## 9. Commit Batches

| Batch | Content | Files |
|-------|---------|-------|
| 1 | Collection element mapping + enum mapping check | `ObjectMappingRuleResolver.cs` |
| 2 | Ambiguous mapping detection (OM007) | `ObjectMappingRuleResolver.cs` |
| 3 | Expression tree null handling + diagnostic tests | `ObjectMappingCodeWriter.cs`, test files |
| 4 | Documentation expansion + `#nullable enable` | Documentation file, `ObjectMappingCodeWriter.cs` |

## 10. Acceptance Criteria

- [ ] Collection element mapping validation implemented with OM008 diagnostic
- [ ] Enum mapping validation implemented with underlying type check
- [ ] Ambiguous mapping detection (OM007) implemented
- [ ] Expression tree null handling consistent with ToTarget
- [ ] `#nullable enable` added to generated code
- [ ] Performance optimization for type compatibility check
- [ ] Inherited property support implemented
- [ ] All diagnostic scenario tests pass
- [ ] Documentation expanded with examples, migration guide, and limitations
