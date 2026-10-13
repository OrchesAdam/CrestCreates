using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

/// <summary>
/// Bounded, deterministic closure validation of a replayable local event's JSON contract. A
/// <c>[LocalEvent(Replayable = true)]</c> event only produces a replayable payload if every
/// reachable serialized member has a closed System.Text.Json contract: scalars, enums, concrete
/// classes/structs (recursively), collections of closed elements, or an explicit typed contract
/// (a <c>[JsonConverter]</c> or a closed <c>[JsonDerivedType]</c> hierarchy). Unsealed
/// <c>object</c>/interface/abstract members are rejected at build time with a member-level path —
/// this is never a runtime fallback or a dynamic resolver.
/// </summary>
internal static class LocalEventJsonClosureValidator
{
    private const int MaxDepth = 32;

    private const string JsonIgnoreAttribute = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string JsonDerivedTypeAttribute = "System.Text.Json.Serialization.JsonDerivedTypeAttribute";
    private const string JsonPolymorphicAttribute = "System.Text.Json.Serialization.JsonPolymorphicAttribute";
    private const string JsonIncludeAttribute = "System.Text.Json.Serialization.JsonIncludeAttribute";

    private static readonly HashSet<string> ClosedScalars = new(StringComparer.Ordinal)
    {
        "global::System.String",
        "global::System.Boolean", "global::System.Byte", "global::System.SByte",
        "global::System.Int16", "global::System.UInt16", "global::System.Int32", "global::System.UInt32",
        "global::System.Int64", "global::System.UInt64", "global::System.Single", "global::System.Double",
        "global::System.Decimal", "global::System.Char",
        "global::System.Guid", "global::System.DateTime", "global::System.DateTimeOffset",
        "global::System.TimeSpan", "global::System.DateOnly", "global::System.TimeOnly",
        "global::System.Uri", "global::System.Version",
        "global::System.Numerics.BigInteger",
        "global::System.Text.Json.JsonElement", "global::System.Text.Json.JsonDocument", "global::System.Text.Json.Nodes.JsonNode",
    };

    /// <summary>
    /// Returns a member-level path to the first reachable member with no closed JSON contract, or
    /// <c>null</c> when the whole serialized shape is closed.
    /// </summary>
    public static string? FindFirstUnsealedMember(ITypeSymbol rootType)
    {
        var visiting = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        return Inspect(rootType, rootType.Name, visiting, 0);
    }

    private static string? Inspect(ITypeSymbol type, string path, HashSet<ITypeSymbol> visiting, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                return Inspect(array.ElementType, path + "[]", visiting, depth + 1);

            case IPointerTypeSymbol or IFunctionPointerTypeSymbol:
                return $"{path} ({type.ToDisplayString()} has no JSON contract)";

            case ITypeParameterSymbol:
                return $"{path} ({type.Name} is an open generic parameter)";

            case INamedTypeSymbol { IsGenericType: true } nullable
                when nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T:
                return Inspect(nullable.TypeArguments[0], path, visiting, depth + 1);

            case INamedTypeSymbol named:
                return InspectNamed(named, path, visiting, depth);

            default:
                return null;
        }
    }

    private static string? InspectNamed(INamedTypeSymbol type, string path, HashSet<ITypeSymbol> visiting, int depth)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return null;
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            return $"{path} ({type.ToDisplayString()} has no closed JSON contract; declare a concrete type)";
        }

        var displayName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (ClosedScalars.Contains(displayName))
        {
            return null;
        }

        // An explicit typed converter on the type itself closes it.
        if (HasAttribute(type, JsonConverterAttribute))
        {
            return null;
        }

        // An explicit [JsonDerivedType] hierarchy closes a polymorphic base ONLY if it has a
        // valid discriminator for round-trip. Check for [JsonPolymorphic] with TypeDiscriminatorPropertyName
        // OR derived types with discriminator tags.
        if (HasAttribute(type, JsonDerivedTypeAttribute))
        {
            if (!HasValidPolymorphicDiscriminator(type))
            {
                return $"{path} ({type.ToDisplayString()} has [JsonDerivedType] but no valid discriminator for round-trip; " +
                       "add [JsonPolymorphic(TypeDiscriminatorPropertyName = ...)] or discriminator tags to [JsonDerivedType])";
            }

            foreach (var derived in GetJsonDerivedTypes(type))
            {
                var offender = Inspect(derived, path + "/" + derived.Name, visiting, depth + 1);
                if (offender is not null)
                {
                    return offender;
                }
            }

            return null;
        }

        // Collection interfaces (IReadOnlyList<T>, IEnumerable<T>, IDictionary<K,V>, etc.) are closed
        // if their element types are closed. Check this BEFORE rejecting interfaces.
        if (TryGetEnumerableElements(type, out var elements))
        {
            foreach (var element in elements)
            {
                var offender = Inspect(element, path + "[]", visiting, depth + 1);
                if (offender is not null)
                {
                    return offender;
                }
            }

            return null;
        }

        if (type.TypeKind == TypeKind.Interface)
        {
            return $"{path} ({type.ToDisplayString()} is an interface with no closed polymorphic contract)";
        }

        if (type.IsAbstract)
        {
            return $"{path} ({type.ToDisplayString()} is abstract with no closed [JsonDerivedType] contract)";
        }

        if (type.IsImplicitlyDeclared && type.BaseType is not null)
        {
            // Anonymous / compiler-generated shapes are not stable replay contracts.
            return $"{path} ({type.ToDisplayString()} is not a stable JSON contract)";
        }

        if (!visiting.Add(type))
        {
            return null;
        }

        try
        {
            var offender = InspectMembers(type, path, visiting, depth);
            if (offender is not null)
            {
                return offender;
            }

            // System.Text.Json flattens the public members of the whole base chain; validate them too.
            for (var baseType = type.BaseType;
                 baseType is not null && baseType.SpecialType == SpecialType.None;
                 baseType = baseType.BaseType)
            {
                offender = InspectMembers(baseType, path, visiting, depth);
                if (offender is not null)
                {
                    return offender;
                }
            }

            return null;
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static string? InspectMembers(INamedTypeSymbol type, string path, HashSet<ITypeSymbol> visiting, int depth)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsStatic)
            {
                continue;
            }

            if (IsAlwaysIgnored(member) || HasAttribute(member, JsonConverterAttribute))
            {
                continue;
            }

            ITypeSymbol? memberType = null;
            if (member is IPropertySymbol property)
            {
                if (property.IsIndexer || property.GetMethod is null)
                {
                    continue;
                }

                if (property.DeclaredAccessibility != Accessibility.Public
                    && !HasAttribute(property, JsonIncludeAttribute))
                {
                    continue;
                }

                memberType = property.Type;
            }
            else if (member is IFieldSymbol field)
            {
                if (field.IsConst || !HasAttribute(field, JsonIncludeAttribute))
                {
                    continue;
                }

                memberType = field.Type;
            }

            if (memberType is null)
            {
                continue;
            }

            var offender = Inspect(memberType, path + "." + member.Name, visiting, depth + 1);
            if (offender is not null)
            {
                return offender;
            }
        }

        return null;
    }

    private static bool TryGetEnumerableElements(INamedTypeSymbol type, out List<ITypeSymbol> elements)
    {
        elements = new List<ITypeSymbol>();

        // Only allow known STJ-supported collection contracts. Custom interfaces that implement
        // IEnumerable<T> cannot be instantiated by STJ and must be rejected.
        if (!IsStjSupportedCollectionType(type))
        {
            return false;
        }

        // Extract element types from generic arguments
        if (type.IsGenericType)
        {
            // Single-parameter collections: IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, List<T>
            if (type.TypeArguments.Length == 1)
            {
                elements.Add(type.TypeArguments[0]);
                return true;
            }

            // Two-parameter collections: IDictionary<K,V>, Dictionary<K,V>, IReadOnlyDictionary<K,V>
            if (type.TypeArguments.Length == 2)
            {
                // For dictionaries, we validate both key and value types
                elements.Add(type.TypeArguments[0]); // key
                elements.Add(type.TypeArguments[1]); // value
                return true;
            }
        }

        return false;
    }

    private static bool IsStjSupportedCollectionType(INamedTypeSymbol type)
    {
        var name = type.Name;
        var ns = type.ContainingNamespace?.ToDisplayString();

        if (ns != "System.Collections.Generic")
        {
            return false;
        }

        // Known STJ-supported collection types
        return name is "IEnumerable" or "ICollection" or "IList" 
            or "IReadOnlyCollection" or "IReadOnlyList" or "List"
            or "IDictionary" or "IReadOnlyDictionary" or "Dictionary";
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSelfAndInterfaces(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            yield return type;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            yield return @interface;
        }
    }

    private static IEnumerable<ITypeSymbol> GetJsonDerivedTypes(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != JsonDerivedTypeAttribute)
            {
                continue;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is ITypeSymbol derived)
                {
                    yield return derived;
                }
            }
        }
    }

    private static bool HasValidPolymorphicDiscriminator(INamedTypeSymbol type)
    {
        // Every [JsonDerivedType] must have a discriminator tag. TypeDiscriminatorPropertyName
        // alone only defines WHERE the discriminator is written, not that each branch has one.
        var derivedAttrs = type.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == JsonDerivedTypeAttribute)
            .ToList();

        if (derivedAttrs.Count == 0)
        {
            return false;
        }

        foreach (var attr in derivedAttrs)
        {
            // [JsonDerivedType(typeof(Derived))] - no discriminator
            // [JsonDerivedType(typeof(Derived), "tag")] - string discriminator
            // [JsonDerivedType(typeof(Derived), 1)] - int discriminator
            if (attr.ConstructorArguments.Length < 2)
            {
                return false;
            }

            var tag = attr.ConstructorArguments[1].Value;
            if (tag is string strTag && !string.IsNullOrEmpty(strTag))
            {
                continue;
            }
            if (tag is int)
            {
                continue;
            }

            // Invalid or missing tag
            return false;
        }

        return true;
    }

    private static bool IsAlwaysIgnored(ISymbol symbol)
    {
        var attribute = symbol.GetAttributes()
            .FirstOrDefault(candidate => candidate.AttributeClass?.ToDisplayString() == JsonIgnoreAttribute);

        if (attribute is null)
        {
            return false;
        }

        // No arguments at all → defaults to Always.
        if (attribute.ConstructorArguments.Length == 0 && attribute.NamedArguments.Length == 0)
        {
            return true;
        }

        // Check constructor argument: [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        // JsonIgnoreCondition.Always = 0
        if (attribute.ConstructorArguments.Length > 0)
        {
            var arg = attribute.ConstructorArguments[0];
            if (IsJsonIgnoreAlways(arg))
            {
                return true;
            }
        }

        // Check named argument: [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == "Condition" && IsJsonIgnoreAlways(named.Value))
            {
                return true;
            }
        }

        // Any other argument → conditional (not Always).
        return false;
    }

    private static bool IsJsonIgnoreAlways(TypedConstant constant)
    {
        // Enum values in Roslyn can be represented as:
        // 1. The underlying numeric value (int, byte, short, etc.)
        // 2. An IFieldSymbol representing the enum member
        
        if (constant.Value is null || constant.Type is null)
        {
            return false;
        }

        // Check if the type is actually resolved (not an error type)
        if (constant.Type.TypeKind == TypeKind.Error)
        {
            // Enum not resolved - can't determine the value
            return false;
        }

        // Case 1: IFieldSymbol representing the enum member
        if (constant.Value is IFieldSymbol fieldSymbol)
        {
            return fieldSymbol.Name == "Always";
        }

        // Case 2: underlying numeric value - try all common numeric types
        return constant.Value switch
        {
            int i => i == 0,
            byte b => b == 0,
            short s => s == 0,
            long l => l == 0,
            _ => false
        };
    }

    private static bool HasAttribute(ISymbol symbol, string attributeMetadataName)
        => symbol.GetAttributes().Any(candidate => candidate.AttributeClass?.ToDisplayString() == attributeMetadataName);
}
