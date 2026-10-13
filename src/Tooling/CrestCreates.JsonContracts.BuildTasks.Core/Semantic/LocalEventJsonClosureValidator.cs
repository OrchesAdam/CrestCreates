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
    private const string JsonIncludeAttribute = "System.Text.Json.Serialization.JsonIncludeAttribute";

    private static readonly HashSet<string> ClosedScalars = new(StringComparer.Ordinal)
    {
        "System.String",
        "System.Boolean", "System.Byte", "System.SByte",
        "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64", "System.Single", "System.Double",
        "System.Decimal", "System.Char",
        "System.Guid", "System.DateTime", "System.DateTimeOffset",
        "System.TimeSpan", "System.DateOnly", "System.TimeOnly",
        "System.Uri", "System.Version",
        "System.Numerics.BigInteger",
        "System.Text.Json.JsonElement", "System.Text.Json.JsonDocument", "System.Text.Json.Nodes.JsonNode",
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

        // An explicit [JsonDerivedType] hierarchy closes a polymorphic base; recurse into the
        // declared derived types so their own members are validated too.
        if (HasAttribute(type, JsonDerivedTypeAttribute))
        {
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

        if (type.TypeKind == TypeKind.Interface)
        {
            return $"{path} ({type.ToDisplayString()} is an interface with no closed polymorphic contract)";
        }

        if (type.IsAbstract)
        {
            return $"{path} ({type.ToDisplayString()} is abstract with no closed [JsonDerivedType] contract)";
        }

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

            if (HasAttribute(member, JsonIgnoreAttribute) || HasAttribute(member, JsonConverterAttribute))
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
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var candidate in EnumerateSelfAndInterfaces(type))
        {
            if (candidate is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic)
            {
                continue;
            }

            if (generic.Name == "IEnumerable"
                && generic.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
            {
                if (seen.Add(generic.TypeArguments[0]))
                {
                    elements.Add(generic.TypeArguments[0]);
                }
            }
        }

        return elements.Count > 0;
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

    private static bool HasAttribute(ISymbol symbol, string attributeMetadataName)
        => symbol.GetAttributes().Any(candidate => candidate.AttributeClass?.ToDisplayString() == attributeMetadataName);
}
