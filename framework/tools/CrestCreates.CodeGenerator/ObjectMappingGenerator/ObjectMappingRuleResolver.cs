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
                var mapping = ResolvePropertyMapping(targetProp, sourceProperties, declaration);
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
