using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.DynamicApiGenerator;

/// <summary>
/// Shared convention-derivation logic for Dynamic API and Compatibility Projection generators.
/// Methods extracted from DynamicApiAotSourceGenerator — signatures and bodies unchanged,
/// only visibility changed from private static to internal static.
/// </summary>
internal static class DynamicApiConventionAnalyzer
{
    internal static ServiceRouteModel ResolveServiceRoute(INamedTypeSymbol serviceType, string serviceName, INamedTypeSymbol? dynamicApiRouteAttribute)
    {
        if (dynamicApiRouteAttribute is not null)
        {
            // Check implementation class first
            var routeAttribute = serviceType.GetAttributes()
                .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, dynamicApiRouteAttribute));
            if (routeAttribute is not null &&
                routeAttribute.ConstructorArguments.Length == 1 &&
                routeAttribute.ConstructorArguments[0].Value is string template &&
                !string.IsNullOrWhiteSpace(template))
            {
                return new ServiceRouteModel(template.Trim('/'), true);
            }

            // Fallback: check primary service interface
            var primaryInterface = serviceType.AllInterfaces
                .FirstOrDefault(i => i.Name == $"I{serviceType.Name}");
            if (primaryInterface is not null)
            {
                var interfaceRouteAttr = primaryInterface.GetAttributes()
                    .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, dynamicApiRouteAttribute));
                if (interfaceRouteAttr is not null &&
                    interfaceRouteAttr.ConstructorArguments.Length == 1 &&
                    interfaceRouteAttr.ConstructorArguments[0].Value is string interfaceTemplate &&
                    !string.IsNullOrWhiteSpace(interfaceTemplate))
                {
                    return new ServiceRouteModel(interfaceTemplate.Trim('/'), true);
                }
            }
        }

        return new ServiceRouteModel(ToKebabCase(serviceName), false);
    }

    internal static string ResolveActionRoute(IMethodSymbol methodSymbol)
    {
        var methodName = TrimAsyncSuffix(methodSymbol.Name);
        var parameters = methodSymbol.Parameters
            .Where(parameter => parameter.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .ToArray();

        return methodName switch
        {
            "Create" => string.Empty,
            "GetById" => "{id}",
            "Get" when HasRequiredScalarRouteParameter(parameters) => $"{{{parameters[0].Name}}}",
            "GetList" => string.Empty,
            "Update" => "{id}",
            "Delete" => "{id}",
            "GetAll" => "all",
            "Count" => "count",
            "Query" => "query",
            _ when methodName.StartsWith("GetBy", StringComparison.Ordinal) && parameters.Length == 1
                => $"by-{ToKebabCase(methodName.Substring("GetBy".Length))}/{{{parameters[0].Name}}}",
            _ when methodName.StartsWith("Get", StringComparison.Ordinal) && parameters.Length == 0
                => ToKebabCase(methodName.Substring("Get".Length)),
            _ when methodName.StartsWith("Get", StringComparison.Ordinal) && parameters.Length == 1 && !HasRequiredScalarRouteParameter(parameters)
                => ToKebabCase(methodName.Substring("Get".Length)),
            _ when methodName.StartsWith("Get", StringComparison.Ordinal) && HasRequiredScalarRouteParameter(parameters)
                => $"{ToKebabCase(methodName.Substring("Get".Length))}/{{{parameters[0].Name}}}",
            _ when methodName.StartsWith("Exists", StringComparison.Ordinal) && parameters.Length == 1
                => $"{ToKebabCase(methodName)}/{{{parameters[0].Name}}}",
            _ => ToKebabCase(methodName)
        };
    }

    private static bool HasRequiredScalarRouteParameter(IReadOnlyList<IParameterSymbol> parameters)
    {
        if (parameters.Count != 1 || !IsScalar(parameters[0].Type))
        {
            return false;
        }

        var param = parameters[0];

        // If parameter has any explicit default value (including null), it's not required
        if (param.HasExplicitDefaultValue)
        {
            return false;
        }

        // Check if parameter is optional (has Optional attribute in IL)
        if (param.IsOptional)
        {
            return false;
        }

        // For string type, also check if the nullable annotation is present
        // In C# 8+, string? might not set HasExplicitDefaultValue but is still optional
        if (param.Type.SpecialType == SpecialType.System_String &&
            param.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return false;
        }

        return true;
    }

    internal static string ResolvePermission(string serviceName, string methodName)
    {
        var normalized = TrimAsyncSuffix(methodName);
        if (normalized == "Create")
        {
            return $"{serviceName}.Create";
        }

        if (normalized == "Update")
        {
            return $"{serviceName}.Update";
        }

        if (normalized == "Delete")
        {
            return $"{serviceName}.Delete";
        }

        if (normalized == "GetById" || normalized == "Get" || normalized.StartsWith("GetBy", StringComparison.Ordinal))
        {
            return $"{serviceName}.Get";
        }

        return $"{serviceName}.Search";
    }

    internal static string TrimServiceName(string serviceTypeName)
    {
        var name = serviceTypeName;
        if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1)
        {
            name = name.Substring(1);
        }

        if (name.EndsWith("AppService", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "AppService".Length);
        }

        return name;
    }

    internal static string TrimAsyncSuffix(string methodName)
    {
        return methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;
    }

    internal static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Replace("\\\"", "\"");
    }

    internal static ParameterSource ResolveParameterSource(
        IParameterSymbol parameter,
        ISet<string> routeTokens,
        string httpMethod,
        ref bool bodyAssigned)
    {
        if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
        {
            return ParameterSource.CancellationToken;
        }

        if (routeTokens.Contains(parameter.Name))
        {
            return ParameterSource.Route;
        }

        // CRUD delete expectedStamp binds from If-Match header
        if (parameter.Name == "expectedStamp" && parameter.Type.SpecialType == SpecialType.System_String)
        {
            return ParameterSource.Header;
        }

        if (!bodyAssigned &&
            (httpMethod == "POST" || httpMethod == "PUT" || httpMethod == "PATCH") &&
            !IsScalar(parameter.Type))
        {
            bodyAssigned = true;
            return ParameterSource.Body;
        }

        return ParameterSource.Query;
    }

    internal static bool IsScalar(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            return IsScalar(namedType.TypeArguments[0]);
        }

        return typeSymbol.SpecialType switch
        {
            SpecialType.System_String => true,
            SpecialType.System_Boolean => true,
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Decimal => true,
            _ => typeSymbol.TypeKind == TypeKind.Enum ||
                 typeSymbol.ToDisplayString() == "System.Guid" ||
                 typeSymbol.ToDisplayString() == "System.DateTime" ||
                 typeSymbol.ToDisplayString() == "System.DateTimeOffset" ||
                  typeSymbol.ToDisplayString() == "System.TimeSpan"
        };
    }

    internal static string ResolveHttpMethod(string methodName)
    {
        var normalized = TrimAsyncSuffix(methodName);
        if (normalized == "Create" || normalized == "Add" || normalized == "Insert" || normalized.StartsWith("Create", StringComparison.Ordinal))
        {
            return "POST";
        }

        if (normalized == "Update" || normalized == "Put" || normalized.StartsWith("Update", StringComparison.Ordinal))
        {
            return "PUT";
        }

        if (normalized == "Delete" || normalized == "Remove" || normalized.StartsWith("Delete", StringComparison.Ordinal))
        {
            return "DELETE";
        }

        if (normalized.StartsWith("Process", StringComparison.Ordinal) ||
            normalized.StartsWith("Return", StringComparison.Ordinal) ||
            normalized.StartsWith("Extend", StringComparison.Ordinal) ||
            normalized == "Query" ||
            normalized == "Search")
        {
            return "POST";
        }

        return "GET";
    }

    /// <summary>
    /// Enumerates the service type followed by all public inherited interfaces.
    /// Used for discovering all contract methods (including inherited CRUD methods).
    /// </summary>
    internal static IEnumerable<INamedTypeSymbol> EnumerateContractTypes(INamedTypeSymbol serviceType)
    {
        yield return serviceType;

        foreach (var inheritedInterface in serviceType.AllInterfaces.Where(interfaceSymbol => interfaceSymbol.DeclaredAccessibility == Accessibility.Public))
        {
            yield return inheritedInterface;
        }
    }

    /// <summary>
    /// Creates a deduplication key for a method based on its name and parameter types.
    /// </summary>
    internal static string CreateMethodKey(IMethodSymbol methodSymbol)
    {
        return $"{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))} )";
    }

    /// <summary>
    /// Builds query property models for a complex query DTO.
    /// Returns public, non-static, settable scalar properties.
    /// </summary>
    internal static ImmutableArray<QueryPropertyModel> BuildQueryProperties(ITypeSymbol parameterType)
    {
        if (parameterType is not INamedTypeSymbol namedType)
        {
            return ImmutableArray<QueryPropertyModel>.Empty;
        }

        var fullyQualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        return namedType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property.DeclaredAccessibility == Accessibility.Public)
            .Where(property => !property.IsStatic && !property.IsReadOnly)
            .Select(property => new QueryPropertyModel(
                property.Name,
                property.Type.ToDisplayString(fullyQualifiedFormat),
                IsScalar(property.Type),
                property.NullableAnnotation == NullableAnnotation.Annotated || IsNullableType(property.Type)))
            .Where(property => property.IsScalar)
            .ToImmutableArray();
    }

    internal static bool IsNullableType(ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
               namedType.TypeArguments.Length == 1;
    }

    /// <summary>
    /// Produces a type expression suitable for typeof() and generic type arguments.
    /// - Nullable value types (int?): uses Nullable&lt;T&gt; form → global::System.Nullable&lt;global::System.Int32&gt;
    /// - Nullable reference types (BookDto?): strips ? suffix → global::MyApp.BookDto
    /// - Non-nullable types: uses the fully-qualified name as-is
    /// </summary>
    internal static string ToTypeOfExpression(ITypeSymbol type, SymbolDisplayFormat? format = null)
    {
        format ??= SymbolDisplayFormat.FullyQualifiedFormat;

        // Nullable value type: int? → Nullable<int>
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && type is INamedTypeSymbol namedType
            && namedType.TypeArguments.Length == 1)
        {
            var underlyingType = namedType.TypeArguments[0];
            var underlyingFq = underlyingType.ToDisplayString(format);
            return $"global::System.Nullable<{underlyingFq}>";
        }

        // Nullable reference type: BookDto? → BookDto (strip ?)
        var displayString = type.ToDisplayString(format);
        if (displayString.EndsWith("?"))
            return displayString.Substring(0, displayString.Length - 1);

        return displayString;
    }
}
