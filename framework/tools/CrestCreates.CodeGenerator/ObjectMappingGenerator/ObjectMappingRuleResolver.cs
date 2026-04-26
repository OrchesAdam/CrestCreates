using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    internal sealed class ObjectMappingRuleResolver
    {
        public ObjectMappingModel Resolve(MappingDeclaration declaration)
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
                var mapping = ResolvePropertyMapping(targetProp, sourceProperties, declaration, model);
                if (mapping != null)
                {
                    model.PropertyMappings.Add(mapping);
                }
            }

            // Check for unmapped target properties (warning)
            var mappedTargetNames = new HashSet<string>(
                model.PropertyMappings
                    .Where(m => !m.IsIgnored)
                    .Select(m => m.TargetProperty.Name));

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
                return CreateValidMapping(sourceProp, targetProp, declaration, model);
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
                return CreateValidMapping(sourceProp, targetProp, declaration, model);
            }

            // Default: same-name matching
            var matchedSource = sourceProperties.FirstOrDefault(p => p.Name == targetProp.Name);
            if (matchedSource != null)
            {
                return CreateValidMapping(matchedSource, targetProp, declaration, model);
            }

            return null;
        }

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

            // Check type compatibility (including collection element types)
            if (!IsTypeCompatible(sourceProp.Type, targetProp.Type, out var needsNullCheck, out var collectionConversion, out var incompatibleElementTypes))
            {
                if (incompatibleElementTypes)
                {
                    // Report OM008 for collection element type incompatibility
                    model.Diagnostics.Add(ObjectMappingDiagnostics.Create(
                        ObjectMappingDiagnostics.MissingElementMapping,
                        declaration.Location,
                        targetProp.Name,
                        GetElementTypeName(sourceProp.Type),
                        GetElementTypeName(targetProp.Type)));
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
            else if (collectionConversion != null)
            {
                mapping.NeedsCollectionConversion = true;
                mapping.CollectionConversionMethod = collectionConversion;
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

        private bool IsTypeCompatible(ITypeSymbol sourceType, ITypeSymbol targetType, out bool needsNullCheck, out string? collectionConversion, out bool incompatibleElementTypes)
        {
            needsNullCheck = false;
            collectionConversion = null;
            incompatibleElementTypes = false;

            // Same type
            if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            {
                return true;
            }

            // Handle collection types - check element type compatibility
            if (TryGetCollectionConversion(sourceType, targetType, out collectionConversion, out var sourceElement, out var targetElement))
            {
                // Recursively check element type compatibility (without collection conversion tracking)
                if (!IsElementTypeCompatible(sourceElement!, targetElement!, out needsNullCheck))
                {
                    collectionConversion = null; // Reset since mapping failed
                    incompatibleElementTypes = true;
                    return false;
                }
                return true;
            }

            // Check if both are collections but with incompatible element types
            if (IsCollectionType(sourceType, out var srcElem) && IsCollectionType(targetType, out var tgtElem))
            {
                // Different collection types but we couldn't determine conversion - check element type
                if (!IsElementTypeCompatible(srcElem!, tgtElem!, out needsNullCheck))
                {
                    incompatibleElementTypes = true;
                    return false;
                }
                // Same element types - we can still convert between collection types
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
            // When source is nullable and target is not, we need a null check
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

            // Handle enum types - compare underlying types
            if (IsEnumCompatible(sourceType, targetType))
            {
                return true;
            }

            // Check implicit conversion
            return HasImplicitConversion(sourceType, targetType);
        }

        private static bool IsEnumCompatible(ITypeSymbol sourceType, ITypeSymbol targetType)
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

        private bool IsElementTypeCompatible(ITypeSymbol sourceElementType, ITypeSymbol targetElementType, out bool needsNullCheck)
        {
            needsNullCheck = false;

            // Same type
            if (SymbolEqualityComparer.Default.Equals(sourceElementType, targetElementType))
            {
                return true;
            }

            // Handle nullable value types for elements
            if (sourceElementType is INamedTypeSymbol sourceNamed && targetElementType is INamedTypeSymbol targetNamed)
            {
                // T? to T for value types
                if (sourceNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                    SymbolEqualityComparer.Default.Equals(sourceNamed.TypeArguments[0], targetElementType))
                {
                    needsNullCheck = true;
                    return true;
                }

                // T to T? for value types
                if (targetNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                    SymbolEqualityComparer.Default.Equals(sourceElementType, targetNamed.TypeArguments[0]))
                {
                    return true;
                }
            }

            // Handle nullable reference types for elements
            if (sourceElementType.NullableAnnotation == NullableAnnotation.Annotated &&
                targetElementType.NullableAnnotation == NullableAnnotation.NotAnnotated &&
                SymbolEqualityComparer.Default.Equals(sourceElementType.WithNullableAnnotation(NullableAnnotation.None), targetElementType))
            {
                needsNullCheck = true;
                return true;
            }

            if (sourceElementType.NullableAnnotation == NullableAnnotation.NotAnnotated &&
                targetElementType.NullableAnnotation == NullableAnnotation.Annotated &&
                SymbolEqualityComparer.Default.Equals(sourceElementType, targetElementType.WithNullableAnnotation(NullableAnnotation.None)))
            {
                return true;
            }

            // Handle enum types - compare underlying types
            if (IsEnumCompatible(sourceElementType, targetElementType))
            {
                return true;
            }

            // Check implicit conversion
            return HasImplicitConversion(sourceElementType, targetElementType);
        }

        private static bool HasImplicitConversion(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            var conversion = Microsoft.CodeAnalysis.CSharp.CSharpCompilation
                .Create("Temp")
                .ClassifyConversion(sourceType, targetType);

            return conversion.IsImplicit;
        }

        private static bool IsCollectionType(ITypeSymbol type, out ITypeSymbol? elementType)
        {
            elementType = null;

            if (type is not INamedTypeSymbol namedType)
            {
                // Check for array
                if (type.TypeKind == TypeKind.Array && type is IArrayTypeSymbol arrayType)
                {
                    elementType = arrayType.ElementType;
                    return true;
                }
                return false;
            }

            // Check for IEnumerable<T>, List<T>, etc.
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

            return false;
        }

        private static bool TryGetCollectionConversion(
            ITypeSymbol sourceType,
            ITypeSymbol targetType,
            out string? conversionMethod,
            out ITypeSymbol? sourceElementType,
            out ITypeSymbol? targetElementType)
        {
            conversionMethod = null;
            sourceElementType = null;
            targetElementType = null;

            if (!IsCollectionType(sourceType, out sourceElementType) || sourceElementType == null)
                return false;

            if (!IsCollectionType(targetType, out targetElementType) || targetElementType == null)
                return false;

            // Same collection type - no conversion needed
            if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
                return false;

            // Determine if conversion is needed based on target type
            if (targetType is INamedTypeSymbol targetNamed)
            {
                if (targetNamed.Name == "List" || targetNamed.Name == "IList" || targetNamed.Name == "ICollection")
                {
                    conversionMethod = "ToList()";
                    return true;
                }
            }

            // Target is array
            if (targetType.TypeKind == TypeKind.Array)
            {
                conversionMethod = "ToArray()";
                return true;
            }

            // For other target types (IEnumerable, IReadOnlyList, etc.) - no conversion needed
            // as they can be assigned directly
            return false;
        }

        private static string GetElementTypeName(ITypeSymbol type)
        {
            if (IsCollectionType(type, out var elementType) && elementType != null)
            {
                return elementType.ToDisplayString();
            }
            return type.ToDisplayString();
        }

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
