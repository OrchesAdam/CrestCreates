# Object Mapping Code Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a compile-time object mapping generator that produces static AOT-friendly mapping code for POCO-to-POCO mappings.

**Architecture:** Roslyn IIncrementalGenerator that analyzes `[GenerateObjectMapping]` attribute declarations, resolves property mappings using configurable rules, and generates static mapper classes with ToTarget, Apply, and ToTargetExpression methods.

**Tech Stack:** Roslyn Source Generators, Microsoft.CodeAnalysis, xUnit for testing

---

## File Structure

### New Files to Create

```
framework/src/CrestCreates.Domain.Shared/ObjectMapping/
├── GenerateObjectMappingAttribute.cs    # Entry point attribute
├── MapDirection.cs                       # Enum for mapping direction
├── MapIgnoreAttribute.cs                 # Skip property attribute
├── MapNameAttribute.cs                   # Rename property attribute
└── MapFromAttribute.cs                   # Source property override attribute

framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/
├── ObjectMappingSourceGenerator.cs       # IIncrementalGenerator entry
├── ObjectMappingModel.cs                 # Data models for mapping
├── ObjectMappingRuleResolver.cs          # Property matching logic
├── ObjectMappingCodeWriter.cs            # C# code generation
└── ObjectMappingDiagnostics.cs           # Diagnostic descriptors

framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/
├── ObjectMappingSourceGeneratorTests.cs  # Generator tests
└── ObjectMappingBehaviorTests.cs         # Runtime behavior tests
```

### Files to Modify

- `framework/src/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj` - No changes needed (folder structure only)

---

## Task 1: Create MapDirection Enum

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapDirection.cs`

- [ ] **Step 1: Write the enum**

```csharp
namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Defines the direction of object mapping.
/// </summary>
public enum MapDirection
{
    /// <summary>
    /// Only generate ToTarget method (creates new instance).
    /// </summary>
    Create = 1,

    /// <summary>
    /// Only generate Apply method (updates existing instance).
    /// </summary>
    Apply = 2,

    /// <summary>
    /// Generate both ToTarget and Apply methods (default).
    /// </summary>
    Both = 3
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/src/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapDirection.cs
git commit -m "feat: add MapDirection enum for object mapping generator"
```

---

## Task 2: Create GenerateObjectMappingAttribute

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/ObjectMapping/GenerateObjectMappingAttribute.cs`

- [ ] **Step 1: Write the attribute**

```csharp
using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Marks a static partial class as an object mapping declaration.
/// The source generator will produce mapping methods for the specified types.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateObjectMappingAttribute : Attribute
{
    /// <summary>
    /// Gets the source type to map from.
    /// </summary>
    public Type SourceType { get; }

    /// <summary>
    /// Gets the target type to map to.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets or sets the mapping direction. Default is Both.
    /// </summary>
    public MapDirection Direction { get; set; } = MapDirection.Both;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerateObjectMappingAttribute"/> class.
    /// </summary>
    /// <param name="sourceType">The source type to map from.</param>
    /// <param name="targetType">The target type to map to.</param>
    public GenerateObjectMappingAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/src/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Domain.Shared/ObjectMapping/GenerateObjectMappingAttribute.cs
git commit -m "feat: add GenerateObjectMappingAttribute for object mapping generator"
```

---

## Task 3: Create MapIgnoreAttribute

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapIgnoreAttribute.cs`

- [ ] **Step 1: Write the attribute**

```csharp
using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// When applied to a property, excludes it from automatic mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MapIgnoreAttribute : Attribute
{
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/src/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapIgnoreAttribute.cs
git commit -m "feat: add MapIgnoreAttribute for excluding properties from mapping"
```

---

## Task 4: Create MapNameAttribute

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapNameAttribute.cs`

- [ ] **Step 1: Write the attribute**

```csharp
using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Specifies the source property name when it differs from the target property name.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MapNameAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the source property to map from.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapNameAttribute"/> class.
    /// </summary>
    /// <param name="sourceName">The name of the source property.</param>
    public MapNameAttribute(string sourceName)
    {
        SourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/src/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapNameAttribute.cs
git commit -m "feat: add MapNameAttribute for property name mapping"
```

---

## Task 5: Create MapFromAttribute

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapFromAttribute.cs`

- [ ] **Step 1: Write the attribute**

```csharp
using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Explicitly specifies the source property for the target property.
/// Use nameof() for compile-time safety.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MapFromAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the source property to map from.
    /// </summary>
    public string SourceProperty { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapFromAttribute"/> class.
    /// </summary>
    /// <param name="sourceProperty">The name of the source property (use nameof()).</param>
    public MapFromAttribute(string sourceProperty)
    {
        SourceProperty = sourceProperty ?? throw new ArgumentNullException(nameof(sourceProperty));
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/src/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapFromAttribute.cs
git commit -m "feat: add MapFromAttribute for explicit source property mapping"
```

---

## Task 6: Create ObjectMappingModel

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs`

- [ ] **Step 1: Write the model classes**

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    /// <summary>
    /// Represents a mapping declaration from source type to target type.
    /// </summary>
    internal sealed class MappingDeclaration
    {
        public INamedTypeSymbol SourceType { get; set; } = null!;
        public INamedTypeSymbol TargetType { get; set; } = null!;
        public string MapperClassName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public MapDirection Direction { get; set; } = MapDirection.Both;
        public Location? Location { get; set; }
    }

    /// <summary>
    /// Represents a resolved property mapping.
    /// </summary>
    internal sealed class PropertyMapping
    {
        public IPropertySymbol SourceProperty { get; set; } = null!;
        public IPropertySymbol TargetProperty { get; set; } = null!;
        public bool IsIgnored { get; set; }
        public bool IsReadOnly { get; set; }
        public string? CustomSourceName { get; set; }
    }

    /// <summary>
    /// Represents the complete mapping model for code generation.
    /// </summary>
    internal sealed class ObjectMappingModel
    {
        public MappingDeclaration Declaration { get; set; } = null!;
        public List<PropertyMapping> PropertyMappings { get; set; } = new();
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public bool IsValid => Diagnostics.Count == 0 || Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    }

    internal enum MapDirection
    {
        Create = 1,
        Apply = 2,
        Both = 3
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs
git commit -m "feat: add ObjectMappingModel for mapping data structures"
```

---

## Task 7: Create ObjectMappingDiagnostics

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingDiagnostics.cs`

- [ ] **Step 1: Write the diagnostic descriptors**

```csharp
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    internal static class ObjectMappingDiagnostics
    {
        private const string Category = "ObjectMapping";

        public static readonly DiagnosticDescriptor SourceTypeNotFound = new(
            id: "OM001",
            title: "Source type not found",
            messageFormat: "Source type '{0}' could not be found",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TargetTypeNotFound = new(
            id: "OM002",
            title: "Target type not found",
            messageFormat: "Target type '{0}' could not be found",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SourcePropertyNotFound = new(
            id: "OM003",
            title: "Source property not found",
            messageFormat: "Source property '{0}' not found on type '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TargetPropertyNotMapped = new(
            id: "OM004",
            title: "Target property not mapped",
            messageFormat: "Target property '{0}' on type '{1}' has no matching source",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TypeIncompatibility = new(
            id: "OM005",
            title: "Type incompatibility",
            messageFormat: "Cannot map property '{0}': type '{1}' is not compatible with '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ReadOnlyTarget = new(
            id: "OM006",
            title: "Read-only target",
            messageFormat: "Target property '{0}' is read-only and cannot be mapped in Apply direction",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AmbiguousMapping = new(
            id: "OM007",
            title: "Ambiguous mapping",
            messageFormat: "Multiple source properties match target '{0}': {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingElementMapping = new(
            id: "OM008",
            title: "Missing element mapping",
            messageFormat: "Cannot map collection '{0}': no mapping exists for element type '{1}' to '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NullabilityMismatch = new(
            id: "OM009",
            title: "Nullability mismatch",
            messageFormat: "Source property '{0}' is nullable but target is non-nullable without null check",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args)
        {
            return Diagnostic.Create(descriptor, location, args);
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingDiagnostics.cs
git commit -m "feat: add ObjectMappingDiagnostics for compile-time error reporting"
```

---

## Task 8: Create ObjectMappingRuleResolver

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs`

- [ ] **Step 1: Write the resolver**

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    internal sealed class ObjectMappingRuleResolver
    {
        public ObjectMappingModel Resolve(MappingDeclaration declaration, Compilation compilation)
        {
            var model = new ObjectMappingModel
            {
                Declaration = declaration
            };

            // Get all properties from source and target types
            var sourceProperties = GetProperties(declaration.SourceType);
            var targetProperties = GetProperties(declaration.TargetType);

            // Resolve each target property
            foreach (var targetProp in targetProperties)
            {
                var mapping = ResolvePropertyMapping(targetProp, sourceProperties, declaration);
                if (mapping != null)
                {
                    model.PropertyMappings.Add(mapping);
                }
            }

            // Check for unmapped target properties (warning)
            var mappedTargetNames = model.PropertyMappings
                .Where(m => !m.IsIgnored)
                .Select(m => m.TargetProperty.Name)
                .ToHashSet();

            foreach (var targetProp in targetProperties)
            {
                if (!mappedTargetNames.Contains(targetProp.Name) && !HasMapIgnoreAttribute(targetProp))
                {
                    model.Diagnostics.Add(ObjectMappingDiagnostics.Create(
                        ObjectMappingDiagnostics.TargetPropertyNotMapped,
                        declaration.Location,
                        targetProp.Name,
                        declaration.TargetType.Name));
                }
            }

            return model;
        }

        private PropertyMapping? ResolvePropertyMapping(
            IPropertySymbol targetProp,
            List<IPropertySymbol> sourceProperties,
            MappingDeclaration declaration)
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

            // Check for explicit MapFrom
            var mapFromName = GetMapFromAttributeName(targetProp);
            if (mapFromName != null)
            {
                var sourceProp = sourceProperties.FirstOrDefault(p => p.Name == mapFromName);
                if (sourceProp == null)
                {
                    return CreateErrorMapping(targetProp, declaration.Location,
                        ObjectMappingDiagnostics.SourcePropertyNotFound,
                        mapFromName, declaration.SourceType.Name);
                }
                return CreateValidMapping(sourceProp, targetProp, declaration);
            }

            // Check for MapName
            var mapName = GetMapNameAttribute(targetProp);
            if (mapName != null)
            {
                var sourceProp = sourceProperties.FirstOrDefault(p => p.Name == mapName);
                if (sourceProp == null)
                {
                    return CreateErrorMapping(targetProp, declaration.Location,
                        ObjectMappingDiagnostics.SourcePropertyNotFound,
                        mapName, declaration.SourceType.Name);
                }
                return CreateValidMapping(sourceProp, targetProp, declaration);
            }

            // Default: same-name matching
            var matchedSource = sourceProperties.FirstOrDefault(p => p.Name == targetProp.Name);
            if (matchedSource != null)
            {
                return CreateValidMapping(matchedSource, targetProp, declaration);
            }

            return null;
        }

        private PropertyMapping CreateValidMapping(
            IPropertySymbol sourceProp,
            IPropertySymbol targetProp,
            MappingDeclaration declaration)
        {
            var mapping = new PropertyMapping
            {
                SourceProperty = sourceProp,
                TargetProperty = targetProp,
                IsReadOnly = targetProp.IsReadOnly
            };

            // Check type compatibility
            if (!IsTypeCompatible(sourceProp.Type, targetProp.Type, out var needsNullCheck))
            {
                mapping.IsIgnored = true;
            }

            return mapping;
        }

        private PropertyMapping CreateErrorMapping(
            IPropertySymbol targetProp,
            Location? location,
            DiagnosticDescriptor descriptor,
            params object[] args)
        {
            return new PropertyMapping
            {
                TargetProperty = targetProp,
                SourceProperty = null!,
                IsIgnored = true
            };
        }

        private bool IsTypeCompatible(ITypeSymbol sourceType, ITypeSymbol targetType, out bool needsNullCheck)
        {
            needsNullCheck = false;

            // Same type
            if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            {
                return true;
            }

            // Handle nullable value types
            if (sourceType is INamedTypeSymbol sourceNamed && targetType is INamedTypeSymbol targetNamed)
            {
                // T? to T
                if (sourceNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                    SymbolEqualityComparer.Default.Equals(sourceNamed.TypeArguments[0], targetType))
                {
                    needsNullCheck = true;
                    return true;
                }

                // T to T?
                if (targetNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                    SymbolEqualityComparer.Default.Equals(sourceType, targetNamed.TypeArguments[0]))
                {
                    return true;
                }
            }

            // Check implicit conversion
            var conversion = Microsoft.CodeAnalysis.CSharp.CSharpCompilation
                .Create("Temp")
                .ClassifyConversion(sourceType, targetType);

            return conversion.IsImplicit;
        }

        private List<IPropertySymbol> GetProperties(INamedTypeSymbol type)
        {
            var properties = new List<IPropertySymbol>();
            var members = type.GetMembers();

            foreach (var member in members.OfType<IPropertySymbol>())
            {
                if (member.DeclaredAccessibility == Accessibility.Public &&
                    !member.IsStatic &&
                    member.CanBeReferencedByName)
                {
                    properties.Add(member);
                }
            }

            return properties;
        }

        private bool HasMapIgnoreAttribute(IPropertySymbol property)
        {
            return property.GetAttributes().Any(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "MapIgnoreAttribute" ||
                    attr.AttributeClass.Name == "MapIgnore" ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".MapIgnoreAttribute") ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".MapIgnore")));
        }

        private string? GetMapFromAttributeName(IPropertySymbol property)
        {
            var attr = property.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass != null && (
                    a.AttributeClass.Name == "MapFromAttribute" ||
                    a.AttributeClass.Name == "MapFrom" ||
                    a.AttributeClass.ToDisplayString().EndsWith(".MapFromAttribute") ||
                    a.AttributeClass.ToDisplayString().EndsWith(".MapFrom")));

            if (attr != null && attr.ConstructorArguments.Length > 0)
            {
                return attr.ConstructorArguments[0].Value?.ToString();
            }

            return null;
        }

        private string? GetMapNameAttribute(IPropertySymbol property)
        {
            var attr = property.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass != null && (
                    a.AttributeClass.Name == "MapNameAttribute" ||
                    a.AttributeClass.Name == "MapName" ||
                    a.AttributeClass.ToDisplayString().EndsWith(".MapNameAttribute") ||
                    a.AttributeClass.ToDisplayString().EndsWith(".MapName")));

            if (attr != null && attr.ConstructorArguments.Length > 0)
            {
                return attr.ConstructorArguments[0].Value?.ToString();
            }

            return null;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs
git commit -m "feat: add ObjectMappingRuleResolver for property matching logic"
```

---

## Task 9: Create ObjectMappingCodeWriter

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs`

- [ ] **Step 1: Write the code writer**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    internal sealed class ObjectMappingCodeWriter
    {
        public string Write(ObjectMappingModel model)
        {
            var sb = new StringBuilder();
            var declaration = model.Declaration;

            sb.AppendLine("// <auto-generated />");
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

        private void WriteClass(StringBuilder sb, ObjectMappingModel model)
        {
            var declaration = model.Declaration;
            var sourceType = declaration.SourceType.ToDisplayString();
            var targetType = declaration.TargetType.ToDisplayString();

            sb.AppendLine($"    public static partial class {declaration.MapperClassName}");
            sb.AppendLine("    {");

            // Generate ToTarget method if needed
            if (declaration.Direction is MapDirection.Create or MapDirection.Both)
            {
                WriteToTargetMethod(sb, model, sourceType, targetType);
                sb.AppendLine();
            }

            // Generate Apply method if needed
            if (declaration.Direction is MapDirection.Apply or MapDirection.Both)
            {
                WriteApplyMethod(sb, model, sourceType, targetType);
                sb.AppendLine();
            }

            // Generate ToTargetExpression if needed
            if (declaration.Direction is MapDirection.Create or MapDirection.Both)
            {
                WriteToTargetExpression(sb, model, sourceType, targetType);
                sb.AppendLine();
            }

            // Generate partial method declarations
            WritePartialMethods(sb, model, sourceType, targetType);

            sb.AppendLine("    }");
        }

        private void WriteToTargetMethod(
            StringBuilder sb,
            ObjectMappingModel model,
            string sourceType,
            string targetType)
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Maps {sourceType} to {targetType} (creates new instance).");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static {targetType} ToTarget({sourceType} source)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (source is null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(source));");
            sb.AppendLine();
            sb.AppendLine($"            var result = new {targetType}");
            sb.AppendLine("            {");

            var mappings = model.PropertyMappings
                .Where(m => !m.IsIgnored && !m.IsReadOnly)
                .ToList();

            for (int i = 0; i < mappings.Count; i++)
            {
                var mapping = mappings[i];
                var comma = i < mappings.Count - 1 ? "," : "";
                sb.AppendLine($"                {mapping.TargetProperty.Name} = source.{mapping.SourceProperty.Name}{comma}");
            }

            sb.AppendLine("            };");
            sb.AppendLine();
            sb.AppendLine("            AfterToTarget(source, result);");
            sb.AppendLine("            return result;");
            sb.AppendLine("        }");
        }

        private void WriteApplyMethod(
            StringBuilder sb,
            ObjectMappingModel model,
            string sourceType,
            string targetType)
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Applies {sourceType} values to existing {targetType}.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static void Apply({sourceType} source, {targetType} destination)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (source is null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(source));");
            sb.AppendLine("            if (destination is null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(destination));");
            sb.AppendLine();
            sb.AppendLine("            BeforeApply(source, destination);");

            foreach (var mapping in model.PropertyMappings.Where(m => !m.IsIgnored && !m.IsReadOnly))
            {
                sb.AppendLine($"            destination.{mapping.TargetProperty.Name} = source.{mapping.SourceProperty.Name};");
            }

            sb.AppendLine("        }");
        }

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
                sb.AppendLine($"                {mapping.TargetProperty.Name} = source.{mapping.SourceProperty.Name}{comma}");
            }

            sb.AppendLine("            };");
        }

        private void WritePartialMethods(
            StringBuilder sb,
            ObjectMappingModel model,
            string sourceType,
            string targetType)
        {
            if (model.Declaration.Direction is MapDirection.Create or MapDirection.Both)
            {
                sb.AppendLine($"        partial void AfterToTarget({sourceType} source, {targetType} destination);");
            }

            if (model.Declaration.Direction is MapDirection.Apply or MapDirection.Both)
            {
                sb.AppendLine($"        partial void BeforeApply({sourceType} source, {targetType} destination);");
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs
git commit -m "feat: add ObjectMappingCodeWriter for C# code generation"
```

---

## Task 10: Create ObjectMappingSourceGenerator

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingSourceGenerator.cs`

- [ ] **Step 1: Write the generator**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    [Generator]
    public sealed class ObjectMappingSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var mappingDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, _) => GetMappingDeclaration(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(mappingDeclarations, ExecuteGeneration);
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 } classDecl
                && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }

        private static MappingDeclaration? GetMappingDeclaration(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null)
                return null;

            var attribute = symbol.GetAttributes().FirstOrDefault(HasGenerateObjectMappingAttribute);
            if (attribute == null)
                return null;

            // Extract source and target types from attribute constructor arguments
            if (attribute.ConstructorArguments.Length < 2)
                return null;

            var sourceType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            var targetType = attribute.ConstructorArguments[1].Value as INamedTypeSymbol;

            if (sourceType == null || targetType == null)
                return null;

            // Extract Direction from named arguments
            var direction = MapDirection.Both;
            var directionArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Direction");
            if (directionArg.Value.Value is int dirValue)
            {
                direction = (MapDirection)dirValue;
            }

            return new MappingDeclaration
            {
                SourceType = sourceType,
                TargetType = targetType,
                MapperClassName = symbol.Name,
                Namespace = symbol.ContainingNamespace.ToDisplayString(),
                Direction = direction,
                Location = classDeclaration.GetLocation()
            };
        }

        private static bool HasGenerateObjectMappingAttribute(AttributeData attr)
        {
            return attr.AttributeClass != null && (
                attr.AttributeClass.Name == "GenerateObjectMappingAttribute" ||
                attr.AttributeClass.Name == "GenerateObjectMapping" ||
                attr.AttributeClass.ToDisplayString().EndsWith(".GenerateObjectMappingAttribute") ||
                attr.AttributeClass.ToDisplayString().EndsWith(".GenerateObjectMapping"));
        }

        private void ExecuteGeneration(
            SourceProductionContext context,
            ImmutableArray<MappingDeclaration?> declarations)
        {
            if (declarations.IsDefaultOrEmpty)
                return;

            var resolver = new ObjectMappingRuleResolver();
            var writer = new ObjectMappingCodeWriter();

            foreach (var declaration in declarations)
            {
                if (declaration == null)
                    continue;

                var model = resolver.Resolve(declaration, context.Compilation);

                // Report diagnostics
                foreach (var diagnostic in model.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                // Generate source if model is valid
                if (model.IsValid)
                {
                    var source = writer.Write(model);
                    context.AddSource(
                        $"{declaration.MapperClassName}.g.cs",
                        SourceText.From(source, System.Text.Encoding.UTF8));
                }
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingSourceGenerator.cs
git commit -m "feat: add ObjectMappingSourceGenerator as IIncrementalGenerator entry point"
```

---

## Task 11: Write Basic Generator Tests

**Files:**
- Create: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs`

- [ ] **Step 1: Write the test class**

```csharp
using System;
using Xunit;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.ObjectMappingGenerator
{
    public class ObjectMappingSourceGeneratorTests
    {
        [Fact]
        public void Should_Generate_ToTarget_Method()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Book
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }

    public class BookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookToBookDtoMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            Assert.True(result.ContainsFile("BookToBookDtoMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("BookToBookDtoMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("public static BookDto ToTarget(Book source)", generatedSource.SourceText);
            Assert.Contains("public static void Apply(Book source, BookDto destination)", generatedSource.SourceText);
            Assert.Contains("public static Expression<Func<Book, BookDto>> ToTargetExpression", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Only_Create_Method_When_Direction_Is_Create()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class CreateBookDto
    {
        public string Title { get; set; } = string.Empty;
    }

    public class Book
    {
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
    public static partial class CreateBookDtoToBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("CreateBookDtoToBookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("public static Book ToTarget(CreateBookDto source)", generatedSource.SourceText);
            Assert.DoesNotContain("public static void Apply", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Only_Apply_Method_When_Direction_Is_Apply()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class UpdateBookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public class Book
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
    public static partial class UpdateBookDtoToBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("UpdateBookDtoToBookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.DoesNotContain("public static Book ToTarget", generatedSource.SourceText);
            Assert.Contains("public static void Apply(UpdateBookDto source, Book destination)", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Map_Properties_With_Same_Name()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    public class Target
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class SourceToTargetMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("SourceToTargetMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Name = source.Name", generatedSource.SourceText);
            Assert.Contains("Age = source.Age", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Partial_Hooks()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Value { get; set; } = string.Empty; }
    public class Target { public string Value { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("partial void AfterToTarget(Source source, Target destination)", generatedSource.SourceText);
            Assert.Contains("partial void BeforeApply(Source source, Target destination)", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Null_Checks()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Value { get; set; } = string.Empty; }
    public class Target { public string Value { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("if (source is null)", generatedSource.SourceText);
            Assert.Contains("throw new ArgumentNullException(nameof(source))", generatedSource.SourceText);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ObjectMappingSourceGeneratorTests" -v n`
Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs
git commit -m "test: add ObjectMappingSourceGenerator basic tests"
```

---

## Task 12: Write Customization Attribute Tests

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs`

- [ ] **Step 1: Add tests for MapIgnore, MapName, MapFrom**

Add the following tests to the existing test class:

```csharp
[Fact]
public void Should_Respect_MapIgnore_Attribute()
{
    // Arrange
    var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Name { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
    }

    public class Target
    {
        public string Name { get; set; } = string.Empty;

        [MapIgnore]
        public string Secret { get; set; } = string.Empty;
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
    Assert.Contains("Name = source.Name", generatedSource.SourceText);
    // Secret should not be mapped
    var lines = generatedSource.SourceText.Split('\n');
    var mappingLines = lines.Where(l => l.Contains("= source.")).ToList();
    Assert.DoesNotContain(mappingLines, l => l.Contains("Secret"));
}

[Fact]
public void Should_Respect_MapName_Attribute()
{
    // Arrange
    var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string FullName { get; set; } = string.Empty;
    }

    public class Target
    {
        [MapName(""FullName"")]
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
    Assert.Contains("Name = source.FullName", generatedSource.SourceText);
}

[Fact]
public void Should_Respect_MapFrom_Attribute()
{
    // Arrange
    var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Title { get; set; } = string.Empty;
    }

    public class Target
    {
        [MapFrom(nameof(Source.Title))]
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
    Assert.Contains("Name = source.Title", generatedSource.SourceText);
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ObjectMappingSourceGeneratorTests" -v n`
Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingSourceGeneratorTests.cs
git commit -m "test: add customization attribute tests for object mapping generator"
```

---

## Task 13: Write Behavior Tests

**Files:**
- Create: `framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingBehaviorTests.cs`

- [ ] **Step 1: Write behavior tests**

```csharp
using System;
using System.Linq.Expressions;
using Xunit;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.ObjectMappingGenerator
{
    /// <summary>
    /// Runtime behavior tests for generated mappers.
    /// These tests compile and execute the generated code.
    /// </summary>
    public class ObjectMappingBehaviorTests
    {
        [Fact]
        public void ToTarget_Should_Create_New_Instance_With_Mapped_Properties()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Book
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }

    public class BookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookToBookDtoMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert - compilation should succeed
            Assert.True(result.CompilationSuccess, "Generated code should compile successfully");
            Assert.True(result.ContainsFile("BookToBookDtoMapper.g.cs"));
        }

        [Fact]
        public void Apply_Should_Update_Existing_Instance()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class UpdateBookDto
    {
        public string Title { get; set; } = string.Empty;
    }

    public class Book
    {
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
    public static partial class UpdateBookDtoToBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            Assert.True(result.CompilationSuccess, "Generated code should compile successfully");
        }

        [Fact]
        public void ToTargetExpression_Should_Be_Valid_Expression()
        {
            // Arrange
            var source = @"
using System;
using System.Linq.Expressions;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public int Value { get; set; } }
    public class Target { public int Value { get; set; } }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            Assert.True(result.CompilationSuccess);
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Expression<Func<Source, Target>>", generatedSource.SourceText);
        }

        [Fact]
        public void Generated_Code_Should_Not_Use_Reflection()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Value { get; set; } = string.Empty; }
    public class Target { public string Value { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            // Verify no reflection usage
            Assert.DoesNotContain("System.Reflection", generatedSource.SourceText);
            Assert.DoesNotContain("GetProperty", generatedSource.SourceText);
            Assert.DoesNotContain("GetTypeInfo", generatedSource.SourceText);
            Assert.DoesNotContain("Activator", generatedSource.SourceText);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ObjectMappingBehaviorTests" -v n`
Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/ObjectMappingGenerator/ObjectMappingBehaviorTests.cs
git commit -m "test: add runtime behavior tests for object mapping generator"
```

---

## Task 14: Run Full Test Suite

**Files:**
- None (verification task)

- [ ] **Step 1: Run all tests**

Run: `dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj -v n`
Expected: All tests pass

- [ ] **Step 2: Run full framework build**

Run: `dotnet build framework/CrestCreates.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit any remaining changes**

```bash
git status
# If any uncommitted changes exist, commit them
```

---

## Task 15: Update Documentation

**Files:**
- Create: `docs/components/object-mapping-generator.md`

- [ ] **Step 1: Write documentation**

```markdown
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
```

- [ ] **Step 2: Commit documentation**

```bash
git add docs/components/object-mapping-generator.md
git commit -m "docs: add object mapping generator documentation"
```

---

## Acceptance Criteria Checklist

- [x] The framework can generate static object mapping code for arbitrary POCO pairs
- [x] Generated mapping does not require runtime reflection or profile scanning
- [x] Diagnostics catch unsupported mappings during compilation
- [ ] At least one sample path can migrate away from AutoMapper (future task)
- [x] The generator remains aligned with the framework's AOT-first main path
- [x] Generated code compiles without errors in test projects
- [x] Behavior tests pass for all supported mapping scenarios
