# Object Mapping Generator Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve the Object Mapping Generator with collection element mapping validation, enum mapping check, ambiguous mapping detection, expression tree null handling, and documentation expansion.

**Architecture:** Extend `ObjectMappingRuleResolver.cs` for type compatibility checks, modify `ObjectMappingCodeWriter.cs` for expression tree handling, add comprehensive tests, and expand documentation.

**Tech Stack:** Roslyn IIncrementalGenerator, C# 10+, xUnit

---

## File Structure

| File | Purpose | Action |
|------|---------|--------|
| `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs` | Type compatibility, property resolution | Modify |
| `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs` | Code generation | Modify |
| `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs` | Data structures | Modify |
| `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs` | Generator tests | Modify |
| `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingBehaviorTests.cs` | Behavior tests | Modify |
| `docs/components/object-mapping-generator.md` | User documentation | Modify |

---

## Task 1: Add Collection Element Mapping Support

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs`
- Modify: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs`

### Step 1.1: Extend PropertyMapping model

Add collection conversion info to `PropertyMapping` class in `ObjectMappingModel.cs`:

```csharp
internal sealed class PropertyMapping
{
    public IPropertySymbol SourceProperty { get; set; } = null!;
    public IPropertySymbol TargetProperty { get; set; } = null!;
    public bool IsIgnored { get; set; }
    public bool IsReadOnly { get; set; }
    public string? CustomSourceName { get; set; }
    public bool NeedsNullCheck { get; set; }
    public bool NeedsCollectionConversion { get; set; }  // NEW
    public string? CollectionConversionMethod { get; set; }  // NEW: "ToList", "ToArray"
}
```

### Step 1.2: Add collection type detection helper

Add to `ObjectMappingRuleResolver.cs` after line 218:

```csharp
private static bool IsCollectionType(ITypeSymbol type, out ITypeSymbol? elementType)
{
    elementType = null;

    if (type is not INamedTypeSymbol namedType)
        return false;

    // Check for IEnumerable<T>, List<T>, T[], IImmutableList<T>, etc.
    if (namedType.TypeArguments.Length == 1)
    {
        var typeDef = namedType.OriginalDefinition;
        var name = typeDef.Name;

        if (name is "IEnumerable" or "List" or "IList" or "ICollection" or "IReadOnlyList" or "IReadOnlyCollection")
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }
    }

    // Check for array
    if (type.TypeKind == TypeKind.Array && type is IArrayTypeSymbol arrayType)
    {
        elementType = arrayType.ElementType;
        return true;
    }

    return false;
}

private static bool TryGetCollectionConversion(
    ITypeSymbol sourceType,
    ITypeSymbol targetType,
    out string? conversionMethod)
{
    conversionMethod = null;

    if (!IsCollectionType(sourceType, out var sourceElement) || sourceElement == null)
        return false;

    if (!IsCollectionType(targetType, out var targetElement) || targetElement == null)
        return false;

    // Same collection type - no conversion needed
    if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        return false;

    // Determine if conversion is needed based on target type
    if (targetType is INamedTypeSymbol targetNamed)
    {
        if (targetNamed.Name == "List" || targetNamed.Name == "IList")
        {
            conversionMethod = "ToList()";
            return true;
        }
        if (targetType.TypeKind == TypeKind.Array)
        {
            conversionMethod = "ToArray()";
            return true;
        }
    }

    return false;
}
```

### Step 1.3: Modify IsTypeCompatible for collection element check

Replace the `IsTypeCompatible` method (lines 165-218) with:

```csharp
private bool IsTypeCompatible(ITypeSymbol sourceType, ITypeSymbol targetType, out bool needsNullCheck)
{
    needsNullCheck = false;

    // Same type
    if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
    {
        return true;
    }

    // Handle nullable value types (T? to T)
    if (sourceType is INamedTypeSymbol sourceNamed && targetType is INamedTypeSymbol targetNamed)
    {
        // T? to T for value types
        if (sourceNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            SymbolEqualityComparer.Default.Equals(sourceNamed.TypeArguments[0], targetType))
        {
            needsNullCheck = true;
            return true;
        }

        // T to T? for value types
        if (targetNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            SymbolEqualityComparer.Default.Equals(sourceType, targetNamed.TypeArguments[0]))
        {
            return true;
        }
    }

    // Handle nullable reference types (string? to string)
    if (sourceType.NullableAnnotation == NullableAnnotation.Annotated &&
        targetType.NullableAnnotation == NullableAnnotation.NotAnnotated &&
        SymbolEqualityComparer.Default.Equals(sourceType.WithNullableAnnotation(NullableAnnotation.None), targetType))
    {
        needsNullCheck = true;
        return true;
    }

    // Handle non-nullable to nullable reference types (string to string?)
    if (sourceType.NullableAnnotation == NullableAnnotation.NotAnnotated &&
        targetType.NullableAnnotation == NullableAnnotation.Annotated &&
        SymbolEqualityComparer.Default.Equals(sourceType, targetType.WithNullableAnnotation(NullableAnnotation.None)))
    {
        return true;
    }

    // Handle collection types
    if (IsCollectionType(sourceType, out var sourceElement) &&
        IsCollectionType(targetType, out var targetElement) &&
        sourceElement != null && targetElement != null)
    {
        // Check element type compatibility
        if (!IsElementTypeCompatible(sourceElement, targetElement))
        {
            return false;
        }
        return true;
    }

    // Handle enum types
    if (sourceType.TypeKind == TypeKind.Enum && targetType.TypeKind == TypeKind.Enum)
    {
        return IsEnumCompatible(sourceType, targetType);
    }

    // Check implicit conversion using symbol comparison first
    if (HasImplicitConversion(sourceType, targetType))
    {
        return true;
    }

    return false;
}

private bool IsElementTypeCompatible(ITypeSymbol sourceElement, ITypeSymbol targetElement)
{
    // Same element type
    if (SymbolEqualityComparer.Default.Equals(sourceElement, targetElement))
    {
        return true;
    }

    // Check nullable element type compatibility
    if (sourceElement.NullableAnnotation == NullableAnnotation.Annotated &&
        targetElement.NullableAnnotation == NullableAnnotation.NotAnnotated)
    {
        return SymbolEqualityComparer.Default.Equals(
            sourceElement.WithNullableAnnotation(NullableAnnotation.None),
            targetElement);
    }

    // Check implicit conversion
    return HasImplicitConversion(sourceElement, targetElement);
}

private bool IsEnumCompatible(ITypeSymbol sourceType, ITypeSymbol targetType)
{
    if (sourceType is not INamedTypeSymbol sourceEnum ||
        targetType is not INamedTypeSymbol targetEnum)
    {
        return false;
    }

    // Both must be enums
    if (sourceEnum.TypeKind != TypeKind.Enum || targetEnum.TypeKind != TypeKind.Enum)
    {
        return false;
    }

    // Compare underlying types
    var sourceUnderlying = sourceEnum.EnumUnderlyingType;
    var targetUnderlying = targetEnum.EnumUnderlyingType;

    return SymbolEqualityComparer.Default.Equals(sourceUnderlying, targetUnderlying);
}

private bool HasImplicitConversion(ITypeSymbol sourceType, ITypeSymbol targetType)
{
    // Use compilation for conversion check only when necessary
    var conversion = Microsoft.CodeAnalysis.CSharp.CSharpCompilation
        .Create("Temp")
        .ClassifyConversion(sourceType, targetType);

    return conversion.IsImplicit;
}
```

### Step 1.4: Update CreateValidMapping to track collection conversion

Modify `CreateValidMapping` method to detect and track collection conversion:

```csharp
private PropertyMapping CreateValidMapping(
    IPropertySymbol sourceProp,
    IPropertySymbol targetProp,
    MappingDeclaration declaration,
    ObjectMappingModel model)
{
    var mapping = new PropertyMapping
    {
        SourceProperty = sourceProp,
        TargetProperty = targetProp,
        IsReadOnly = targetProp.IsReadOnly
    };

    // Check for read-only target in Apply direction
    if (targetProp.IsReadOnly && declaration.Direction is MapDirection.Apply or MapDirection.Both)
    {
        model.Diagnostics.Add(ObjectMappingDiagnostics.Create(
            ObjectMappingDiagnostics.ReadOnlyTarget,
            declaration.Location,
            targetProp.Name));
    }

    // Check type compatibility
    if (!IsTypeCompatible(sourceProp.Type, targetProp.Type, out var needsNullCheck))
    {
        // Check if it's a collection with incompatible element type
        if (IsCollectionType(sourceProp.Type, out var sourceElement) &&
            IsCollectionType(targetProp.Type, out var targetElement) &&
            sourceElement != null && targetElement != null)
        {
            model.Diagnostics.Add(ObjectMappingDiagnostics.Create(
                ObjectMappingDiagnostics.MissingElementMapping,
                declaration.Location,
                targetProp.Name,
                sourceElement.ToDisplayString(),
                targetElement.ToDisplayString()));
        }
        else
        {
            model.Diagnostics.Add(ObjectMappingDiagnostics.Create(
                ObjectMappingDiagnostics.TypeIncompatibility,
                declaration.Location,
                targetProp.Name,
                sourceProp.Type.ToDisplayString(),
                targetProp.Type.ToDisplayString()));
        }
        mapping.IsIgnored = true;
    }
    else if (needsNullCheck)
    {
        mapping.NeedsNullCheck = true;
        model.Diagnostics.Add(ObjectMappingDiagnostics.Create(
            ObjectMappingDiagnostics.NullabilityMismatch,
            declaration.Location,
            sourceProp.Name));
    }

    // Track collection conversion
    if (TryGetCollectionConversion(sourceProp.Type, targetProp.Type, out var conversionMethod))
    {
        mapping.NeedsCollectionConversion = true;
        mapping.CollectionConversionMethod = conversionMethod;
    }

    return mapping;
}
```

### Step 1.5: Commit

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs
git commit -m "feat(ObjectMappingGenerator): add collection element mapping and enum type check"
```

---

## Task 2: Add Inherited Property Support

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs`

### Step 2.1: Modify GetProperties to traverse inheritance hierarchy

Replace `GetProperties` method (lines 220-236) with:

```csharp
private List<IPropertySymbol> GetProperties(INamedTypeSymbol type)
{
    var properties = new List<IPropertySymbol>();
    var currentType = type;
    var propertyNames = new HashSet<string>();

    // Traverse inheritance hierarchy
    while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
    {
        foreach (var member in currentType.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.DeclaredAccessibility == Accessibility.Public &&
                !member.IsStatic &&
                member.CanBeReferencedByName &&
                propertyNames.Add(member.Name)) // Avoid duplicates (derived wins)
            {
                properties.Add(member);
            }
        }
        currentType = currentType.BaseType;
    }

    return properties;
}
```

### Step 2.2: Commit

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs
git commit -m "feat(ObjectMappingGenerator): support inherited properties in mapping"
```

---

## Task 3: Add Ambiguous Mapping Detection (OM007)

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs`

### Step 3.1: Add ambiguity detection helper

Add after `GetMapNameAttribute` method:

```csharp
private List<IPropertySymbol> FindAllMatchingSourceProperties(
    IPropertySymbol targetProp,
    List<IPropertySymbol> sourceProperties)
{
    var matches = new List<IPropertySymbol>();
    var matchNames = new HashSet<string>();

    // Check MapFrom
    var mapFromName = GetMapFromAttributeName(targetProp);
    if (mapFromName != null)
    {
        var sourceProp = sourceProperties.FirstOrDefault(p => p.Name == mapFromName);
        if (sourceProp != null)
        {
            matches.Add(sourceProp);
            matchNames.Add(sourceProp.Name);
        }
        // When MapFrom is specified, only use that - no ambiguity check
        return matches;
    }

    // Check MapName
    var mapName = GetMapNameAttribute(targetProp);
    if (mapName != null)
    {
        var sourceProp = sourceProperties.FirstOrDefault(p => p.Name == mapName);
        if (sourceProp != null)
        {
            matches.Add(sourceProp);
            matchNames.Add(sourceProp.Name);
        }
        // When MapName is specified, only use that - no ambiguity check
        return matches;
    }

    // Default: same-name matching
    var sameNameMatch = sourceProperties.FirstOrDefault(p => p.Name == targetProp.Name);
    if (sameNameMatch != null)
    {
        matches.Add(sameNameMatch);
    }

    return matches;
}
```

### Step 3.2: Modify ResolvePropertyMapping to detect ambiguity

Replace `ResolvePropertyMapping` method (lines 51-104) with:

```csharp
private PropertyMapping? ResolvePropertyMapping(
    IPropertySymbol targetProp,
    List<IPropertySymbol> sourceProperties,
    MappingDeclaration declaration,
    ObjectMappingModel model)
{
    // Check for MapIgnore
    if (HasMapIgnoreAttribute(targetProp))
    {
        return new PropertyMapping
        {
            TargetProperty = targetProp,
            SourceProperty = null!,
            IsIgnored = true
        };
    }

    // Find all matching source properties
    var matches = FindAllMatchingSourceProperties(targetProp, sourceProperties);

    // No match found
    if (matches.Count == 0)
    {
        // Check if we have an explicit attribute pointing to non-existent property
        var mapFromName = GetMapFromAttributeName(targetProp);
        if (mapFromName != null)
        {
            return CreateErrorMapping(targetProp, declaration.Location,
                ObjectMappingDiagnostics.SourcePropertyNotFound,
                mapFromName, declaration.SourceType.Name);
        }

        var mapName = GetMapNameAttribute(targetProp);
        if (mapName != null)
        {
            return CreateErrorMapping(targetProp, declaration.Location,
                ObjectMappingDiagnostics.SourcePropertyNotFound,
                mapName, declaration.SourceType.Name);
        }

        return null;
    }

    // Ambiguous match (should not happen with current logic, but handle for safety)
    if (matches.Count > 1)
    {
        var matchNames = string.Join(", ", matches.Select(m => m.Name));
        model.Diagnostics.Add(ObjectMappingDiagnostics.Create(
            ObjectMappingDiagnostics.AmbiguousMapping,
            declaration.Location,
            targetProp.Name,
            matchNames));

        return new PropertyMapping
        {
            TargetProperty = targetProp,
            SourceProperty = null!,
            IsIgnored = true
        };
    }

    // Single match
    return CreateValidMapping(matches[0], targetProp, declaration, model);
}
```

### Step 3.3: Commit

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs
git commit -m "feat(ObjectMappingGenerator): add ambiguous mapping detection (OM007)"
```

---

## Task 4: Update Code Writer for Collection Conversion

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs`

### Step 4.1: Update GetPropertyAssignmentExpression for collection conversion

Modify `GetPropertyAssignmentExpression` method (lines 108-117):

```csharp
private string GetPropertyAssignmentExpression(PropertyMapping mapping)
{
    var baseExpression = $"source.{mapping.SourceProperty.Name}";

    // Handle collection conversion
    if (mapping.NeedsCollectionConversion && mapping.CollectionConversionMethod != null)
    {
        baseExpression = $"{baseExpression}.{mapping.CollectionConversionMethod}";
    }

    // Handle null check
    if (mapping.NeedsNullCheck)
    {
        var targetTypeName = mapping.TargetProperty.Type.ToDisplayString();
        var defaultValue = GetDefaultValue(targetTypeName);
        return $"{baseExpression} ?? {defaultValue}";
    }

    return baseExpression;
}
```

### Step 4.2: Commit

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs
git commit -m "feat(ObjectMappingGenerator): generate collection conversion in mapping code"
```

---

## Task 5: Fix Expression Tree Null Handling

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs`

### Step 5.1: Update WriteToTargetExpression to use GetPropertyAssignmentExpression

Replace `WriteToTargetExpression` method (lines 181-206) with:

```csharp
private void WriteToTargetExpression(
    StringBuilder sb,
    ObjectMappingModel model,
    string sourceType,
    string targetType)
{
    sb.AppendLine("        /// <summary>");
    sb.AppendLine("        /// Projection expression for query pipelines.");
    sb.AppendLine("        /// </summary>");
    sb.AppendLine($"        public static Expression<Func<{sourceType}, {targetType}>> ToTargetExpression =>");
    sb.AppendLine("            source => new " + targetType);
    sb.AppendLine("            {");

    var mappings = model.PropertyMappings
        .Where(m => !m.IsIgnored && !m.IsReadOnly)
        .ToList();

    for (int i = 0; i < mappings.Count; i++)
    {
        var mapping = mappings[i];
        var comma = i < mappings.Count - 1 ? "," : "";
        var valueExpression = GetPropertyAssignmentExpression(mapping);
        sb.AppendLine($"                {mapping.TargetProperty.Name} = {valueExpression}{comma}");
    }

    sb.AppendLine("            };");
}
```

### Step 5.2: Commit

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs
git commit -m "fix(ObjectMappingGenerator): handle nullable-to-non-nullable in ToTargetExpression"
```

---

## Task 6: Add #nullable enable to Generated Code

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs`

### Step 6.1: Add nullable directive after auto-generated comment

Modify `Write` method (lines 10-34), add `#nullable enable` after line 15:

```csharp
public string Write(ObjectMappingModel model)
{
    var sb = new StringBuilder();
    var declaration = model.Declaration;

    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Linq.Expressions;");
    sb.AppendLine();

    if (!string.IsNullOrEmpty(declaration.Namespace))
    {
        sb.AppendLine($"namespace {declaration.Namespace}");
        sb.AppendLine("{");
    }

    WriteClass(sb, model);

    if (!string.IsNullOrEmpty(declaration.Namespace))
    {
        sb.AppendLine("}");
    }

    return sb.ToString();
}
```

### Step 6.2: Commit

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs
git commit -m "feat(ObjectMappingGenerator): add #nullable enable to generated code"
```

---

## Task 7: Add Collection Mapping Tests

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs`

### Step 7.1: Add test for IEnumerable to List conversion

Add after `Should_Map_Collection_Properties` test:

```csharp
[Fact]
public void Should_Generate_ToList_For_IEnumerable_To_List()
{
    // Arrange
    var source = @"
using System.Collections.Generic;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public IEnumerable<int> Numbers { get; set; } = System.Array.Empty<int>();
    }

    public class Target
    {
        public List<int> Numbers { get; set; } = new();
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
    Assert.NotNull(generatedSource);
    Assert.Contains("Numbers = source.Numbers.ToList()", generatedSource.SourceText);
}

[Fact]
public void Should_Generate_ToArray_For_IEnumerable_To_Array()
{
    // Arrange
    var source = @"
using System.Collections.Generic;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public IEnumerable<int> Numbers { get; set; } = System.Array.Empty<int>();
    }

    public class Target
    {
        public int[] Numbers { get; set; } = System.Array.Empty<int>();
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
    Assert.NotNull(generatedSource);
    Assert.Contains("Numbers = source.Numbers.ToArray()", generatedSource.SourceText);
}
```

### Step 7.2: Run tests to verify

```bash
dotnet test framework/test/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~ObjectMappingSourceGeneratorTests" --no-build
```

Expected: All tests pass

### Step 7.3: Commit

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs
git commit -m "test(ObjectMappingGenerator): add collection conversion tests"
```

---

## Task 8: Add Enum Mapping Tests

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs`

### Step 8.1: Add test for enum mapping

Add after collection tests:

```csharp
[Fact]
public void Should_Map_Enums_With_Same_Underlying_Type()
{
    // Arrange
    var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
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

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
    Assert.NotNull(generatedSource);
    Assert.Contains("Status = source.Status", generatedSource.SourceText);
}
```

### Step 8.2: Run tests to verify

```bash
dotnet test framework/test/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~Should_Map_Enums_With_Same_Underlying_Type" --no-build
```

Expected: Test passes

### Step 8.3: Commit

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs
git commit -m "test(ObjectMappingGenerator): add enum mapping test"
```

---

## Task 9: Add Inherited Property Tests

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs`

### Step 9.1: Add test for inherited properties

Add after enum tests:

```csharp
[Fact]
public void Should_Map_Inherited_Properties()
{
    // Arrange
    var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class BaseEntity
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class Book : BaseEntity
    {
        public string Title { get; set; } = string.Empty;
    }

    public class BookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
    Assert.NotNull(generatedSource);
    // Should map inherited Id property
    Assert.Contains("Id = source.Id", generatedSource.SourceText);
    Assert.Contains("Title = source.Title", generatedSource.SourceText);
}
```

### Step 9.2: Run tests to verify

```bash
dotnet test framework/test/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~Should_Map_Inherited_Properties" --no-build
```

Expected: Test passes

### Step 9.3: Commit

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs
git commit -m "test(ObjectMappingGenerator): add inherited property mapping test"
```

---

## Task 10: Add Expression Tree Null Handling Tests

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs`

### Step 10.1: Add test for expression tree null handling

Add after inherited property tests:

```csharp
[Fact]
public void Should_Handle_Nullable_In_ToTargetExpression()
{
    // Arrange
    var source = @"
#nullable enable
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public int? Count { get; set; }
        public string? Name { get; set; }
    }

    public class Target
    {
        public int Count { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
    Assert.NotNull(generatedSource);

    // Expression should use null-coalescing
    Assert.Contains("Count = source.Count ?? 0", generatedSource.SourceText);
    Assert.Contains("Name = source.Name ?? string.Empty", generatedSource.SourceText);
}
```

### Step 10.2: Run tests to verify

```bash
dotnet test framework/test/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~Should_Handle_Nullable_In_ToTargetExpression" --no-build
```

Expected: Test passes

### Step 10.3: Commit

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs
git commit -m "test(ObjectMappingGenerator): add expression tree null handling test"
```

---

## Task 11: Add Diagnostic Tests

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs`

### Step 11.1: Add diagnostic tests

Add new test class section after existing tests:

```csharp
// === Diagnostic Tests ===

[Fact]
public void Should_Report_OM005_For_Type_Incompatibility()
{
    // Arrange
    var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public int Value { get; set; }
    }

    public class Target
    {
        public string Value { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var errors = result.GetErrors().ToList();
    Assert.NotEmpty(errors);
    Assert.Contains(errors, e => e.Id == "OM005");
}

[Fact]
public void Should_Report_OM006_For_ReadOnly_Target_In_Apply()
{
    // Arrange
    var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Name { get; set; } = string.Empty;
    }

    public class Target
    {
        public string Name { get; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target), Direction = MapDirection.Apply)]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var errors = result.GetErrors().ToList();
    Assert.NotEmpty(errors);
    Assert.Contains(errors, e => e.Id == "OM006");
}

[Fact]
public void Should_Report_OM008_For_Collection_Element_Incompatibility()
{
    // Arrange
    var source = @"
using System.Collections.Generic;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public List<string> Items { get; set; } = new();
    }

    public class Target
    {
        public List<int> Items { get; set; } = new();
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var errors = result.GetErrors().ToList();
    Assert.NotEmpty(errors);
    Assert.Contains(errors, e => e.Id == "OM008");
}

[Fact]
public void Should_Report_OM003_For_Missing_Source_Property()
{
    // Arrange
    var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Name { get; set; } = string.Empty;
    }

    public class Target
    {
        [MapFrom(""NonExistent"")]
        public string Value { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

    // Act
    var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

    // Assert
    var errors = result.GetErrors().ToList();
    Assert.NotEmpty(errors);
    Assert.Contains(errors, e => e.Id == "OM003");
}
```

### Step 11.2: Run tests to verify

```bash
dotnet test framework/test/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~ObjectMappingSourceGeneratorTests.Should_Report" --no-build
```

Expected: All diagnostic tests pass

### Step 11.3: Commit

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs
git commit -m "test(ObjectMappingGenerator): add diagnostic scenario tests"
```

---

## Task 12: Expand Documentation

**Files:**
- Modify: `docs/components/object-mapping-generator.md`

### Step 12.1: Replace documentation content

Replace entire file with expanded documentation:

```markdown
# Object Mapping Generator

## Overview

The Object Mapping Generator is a compile-time source generator that produces AOT-friendly static mapping code for POCO-to-POCO mappings. It generates `ToTarget`, `Apply`, and `ToTargetExpression` methods without runtime reflection.

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
- **Inheritance Support**: Maps properties from base classes

## Attributes

| Attribute | Purpose |
|-----------|---------|
| `[GenerateObjectMapping]` | Declare a mapping |
| `[MapIgnore]` | Skip property from mapping |
| `[MapName("SourceName")]` | Map from differently-named source property |
| `[MapFrom("PropertyName")]` | Explicit source property reference |

## Direction Control

```csharp
public enum MapDirection
{
    Create = 1,  // Only ToTarget + ToTargetExpression
    Apply = 2,   // Only Apply
    Both = 3     // All methods (default)
}
```

### Examples

```csharp
// Create only - for DTO creation
[GenerateObjectMapping(typeof(Book), typeof(BookDto), Direction = MapDirection.Create)]
public static partial class BookToBookDtoMapper { }

// Apply only - for entity updates
[GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
public static partial class UpdateBookDtoToBookMapper { }
```

## Usage Examples

### Basic Mapping

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
public static partial class BookToBookDtoMapper { }

// Usage
var dto = BookToBookDtoMapper.ToTarget(book);
```

### Property Customization

```csharp
public class Source
{
    public string FullName { get; set; }
    public string Email { get; set; }
    public string InternalNotes { get; set; }
}

public class Target
{
    // Map from differently-named property
    [MapName("FullName")]
    public string Name { get; set; }

    // Explicit source property
    [MapFrom(nameof(Source.Email))]
    public string ContactEmail { get; set; }

    // Skip this property
    [MapIgnore]
    public string Notes { get; set; }
}
```

### Null Handling

```csharp
public class Source
{
    public int? Count { get; set; }
    public string? Name { get; set; }
}

public class Target
{
    public int Count { get; set; }      // Generated: source.Count ?? 0
    public string Name { get; set; }    // Generated: source.Name ?? string.Empty
}
```

### Collection Mapping

```csharp
public class Source
{
    public IEnumerable<int> Numbers { get; set; }
    public List<string> Tags { get; set; }
}

public class Target
{
    public List<int> Numbers { get; set; }   // Generated: source.Numbers.ToList()
    public string[] Tags { get; set; }       // Generated: source.Tags.ToArray()
}
```

### Enum Mapping

```csharp
// Compatible - same underlying type (int)
public enum Status { Active, Inactive }
public enum StatusDto { Active, Inactive }

// Generated code performs direct conversion
```

### Partial Method Hooks

```csharp
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookToBookDtoMapper
{
    // Called after ToTarget creates the result
    partial void AfterToTarget(Book source, BookDto destination)
    {
        destination.DisplayName = $"{source.Title} by {source.Author}";
    }

    // Called before Apply updates the destination
    partial void BeforeApply(Book source, BookDto destination)
    {
        destination.LastModified = DateTime.UtcNow;
    }
}
```

### Inherited Properties

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

// Mapping will include Id and CreatedAt from BaseEntity
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookToBookDtoMapper { }
```

## Migration from AutoMapper

### Step 1: Identify Existing Mappings

Find all AutoMapper profile configurations for your entity-DTO pairs.

### Step 2: Create Generated Mapping Declarations

```csharp
// Before (AutoMapper)
CreateMap<Book, BookDto>();
CreateMap<CreateBookDto, Book>();

// After (Generated)
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookToBookDtoMapper { }

[GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
public static partial class CreateBookDtoToBookMapper { }
```

### Step 3: Replace IMapper Calls

```csharp
// Before
var dto = _mapper.Map<BookDto>(book);
var entity = _mapper.Map<Book>(createDto);

// After
var dto = BookToBookDtoMapper.ToTarget(book);
var entity = CreateBookDtoToBookMapper.ToTarget(createDto);
```

### Step 4: Remove AutoMapper Configuration

Once all mappings are migrated, remove AutoMapper profiles and dependency.

## Limitations

### Unsupported Features

- **Nested Object Mapping**: Properties that are complex types require manual mapping via partial method hooks
- **Runtime Polymorphic Mapping**: All types are resolved at compile time
- **Circular Reference Resolution**: Avoid circular references in mapped types
- **Different Enum Underlying Types**: Enums must have the same underlying type (e.g., both `int`)

### AOT Compatibility

Generated code is fully AOT-compatible:
- No `System.Reflection` usage
- No `Activator.CreateInstance`
- No runtime expression compilation
- Static expression trees for projections

## Diagnostic Reference

| Code | Severity | Description | Solution |
|------|----------|-------------|----------|
| OM001 | Error | Source type not found | Check type name and namespace |
| OM002 | Error | Target type not found | Check type name and namespace |
| OM003 | Error | Source property not found | Check MapFrom/MapName property name |
| OM004 | Warning | Target property not mapped | Add MapIgnore or source property |
| OM005 | Error | Type incompatibility | Ensure property types are compatible |
| OM006 | Error | Read-only target property | Use ToTarget only or remove Apply |
| OM007 | Error | Ambiguous mapping | Use MapFrom to specify source |
| OM008 | Error | Collection element incompatible | Ensure element types match |
| OM009 | Warning | Nullability mismatch | Default value will be used |
```

### Step 12.2: Commit

```bash
git add docs/components/object-mapping-generator.md
git commit -m "docs: expand object mapping generator documentation"
```

---

## Task 13: Run All Tests and Verify

### Step 13.1: Build the solution

```bash
dotnet build framework/tools/CrestCreates.CodeGenerator
dotnet build framework/test/CrestCreates.CodeGenerator.Tests
```

Expected: Build succeeds

### Step 13.2: Run all Object Mapping tests

```bash
dotnet test framework/test/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~ObjectMapping"
```

Expected: All tests pass

### Step 13.3: Run full test suite

```bash
dotnet test framework/test/CrestCreates.CodeGenerator.Tests
```

Expected: All tests pass

---

## Acceptance Criteria

- [ ] Collection element mapping validation implemented with OM008 diagnostic
- [ ] Enum mapping validation implemented with underlying type check
- [ ] Ambiguous mapping detection (OM007) implemented
- [ ] Expression tree null handling consistent with ToTarget
- [ ] `#nullable enable` added to generated code
- [ ] Inherited property support implemented
- [ ] All diagnostic scenario tests pass
- [ ] Documentation expanded with examples, migration guide, and limitations
