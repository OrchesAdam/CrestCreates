using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.CapabilityEndpointGenerator;

/// <summary>
/// Normalizes Level 2 HTTP method attributes ([Post], [Get], [Put], [Delete], [Patch])
/// into the unified <see cref="CapabilityEndpointSpecRecord"/> model.
/// </summary>
internal static class CapabilityEndpointSpecNormalizer
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private const string CapabilityEndpointSetMetadataName =
        "CrestCreates.DynamicApi.CapabilityEndpointSetAttribute";

    /// <summary>
    /// Normalizes a Level 2 HTTP method attribute into a <see cref="CapabilityEndpointSpecRecord"/>.
    /// </summary>
    /// <param name="ctx">The generator attribute syntax context.</param>
    /// <param name="httpMethodValue">
    /// 1=Get, 2=Post, 3=Put, 4=Patch, 5=Delete (matching <see cref="CapabilityEndpointHttpMethod"/>).
    /// </param>
    public static CapabilityEndpointSpecRecord? Normalize(
        GeneratorAttributeSyntaxContext ctx,
        int httpMethodValue)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        // The matched attribute should be present in ctx.Attributes.
        if (ctx.Attributes.IsDefaultOrEmpty)
            return null;

        var attr = ctx.Attributes[0];
        var ctorArgs = attr.ConstructorArguments;

        if (ctorArgs.Length < 1)
            return null;

        var capabilityId = ctorArgs[0].Value as string;
        if (string.IsNullOrEmpty(capabilityId))
            return null;

        var route = ctorArgs.Length > 1
            ? (ctorArgs[1].Value as string) ?? string.Empty
            : string.Empty;

        var namedArgs = attr.NamedArguments;

        // --- Common named arguments ---
        var capabilityVersion = GetNamedIntArg(namedArgs, "CapabilityVersion");
        var authValue = GetNamedIntArg(namedArgs, "Auth");
        var successStatusCode = GetNamedIntArg(namedArgs, "SuccessStatusCode");
        var operationId = GetNamedStringArg(namedArgs, "OperationId");
        var summary = GetNamedStringArg(namedArgs, "Summary");
        var description = GetNamedStringArg(namedArgs, "Description");
        var deprecated = GetNamedBoolArg(namedArgs, "Deprecated");

        // --- Container class info ---
        var containerType = classSymbol.ContainingType;
        string containerClassName;
        string containerNamespace;
        bool isNested;

        if (containerType is not null)
        {
            containerClassName = containerType.Name;
            containerNamespace = containerType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            isNested = true;
        }
        else
        {
            containerClassName = classSymbol.Name;
            containerNamespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            isNested = false;
        }

        // --- Extract container's [CapabilityEndpointSet] for defaults ---
        string? routePrefix = null;
        string? groupName = null;
        ImmutableArray<string> tags = ImmutableArray<string>.Empty;

        if (containerType is not null)
        {
            foreach (var containerAttr in containerType.GetAttributes())
            {
                if (containerAttr.AttributeClass is null)
                    continue;

                if (containerAttr.AttributeClass.ToDisplayString() == CapabilityEndpointSetMetadataName)
                {
                    routePrefix = GetNamedStringArg(containerAttr.NamedArguments, "RoutePrefix");
                    groupName = GetNamedStringArg(containerAttr.NamedArguments, "GroupName");
                    tags = GetNamedStringArrayArg(containerAttr.NamedArguments, "Tags");
                    break;
                }
            }
        }

        // --- Normalize route pattern ---
        var routePattern = NormalizeRoutePattern(routePrefix, route);

        // --- Auto-rule: SuccessStatusCode ---
        if (successStatusCode <= 0)
        {
            // POST (value 2) → 201, others → 200
            successStatusCode = httpMethodValue == 2 ? 201 : 200;
        }

        // --- Build input bindings from Body / Input properties ---
        var inputs = BuildInputBindings(attr, route, httpMethodValue);

        return new CapabilityEndpointSpecRecord
        {
            CapabilityId = capabilityId!,
            HttpMethodValue = httpMethodValue,
            RoutePattern = routePattern,
            CapabilityVersion = capabilityVersion,
            AuthorizationModeValue = authValue,
            SuccessStatusCode = successStatusCode,
            OperationId = operationId,
            GroupName = groupName,
            Tags = tags,
            Summary = summary,
            Description = description,
            Deprecated = deprecated,
            SpecClassName = classSymbol.Name,
            ContainerClassName = containerClassName,
            ContainerNamespace = containerNamespace,
            IsNested = isNested,
            Inputs = inputs
        };
    }

    private static string NormalizeRoutePattern(string? routePrefix, string route)
    {
        var prefix = (routePrefix ?? string.Empty).TrimEnd('/');
        var path = (route ?? string.Empty).TrimStart('/');

        string result;
        if (string.IsNullOrEmpty(prefix))
            result = path;
        else if (string.IsNullOrEmpty(path))
            result = prefix;
        else
            result = prefix + "/" + path;

        if (!result.StartsWith("/", StringComparison.Ordinal))
            result = "/" + result;

        return result;
    }

    private static ImmutableArray<CapabilityEndpointInputRecord> BuildInputBindings(
        AttributeData attr,
        string route,
        int httpMethodValue)
    {
        var builder = ImmutableArray.CreateBuilder<CapabilityEndpointInputRecord>();
        var namedArgs = attr.NamedArguments;

        // Extract Body type → InputBinding with Source=Body
        var bodyType = GetNamedTypeArg(namedArgs, "Body");
        if (bodyType is not null)
        {
            builder.Add(new CapabilityEndpointInputRecord
            {
                TypeName = bodyType.ToDisplayString(FullyQualifiedFormat),
                Name = "body",
                SourceValue = 3, // Body
                Required = true
            });
        }

        // Extract Input type → InputBinding with Source=Route
        var inputType = GetNamedTypeArg(namedArgs, "Input");
        if (inputType is not null)
        {
            var inputName = GetNamedStringArg(namedArgs, "InputName");
            var name = !string.IsNullOrEmpty(inputName)
                ? inputName!
                : ExtractFirstRouteTokenName(route) ?? "input";

            builder.Add(new CapabilityEndpointInputRecord
            {
                TypeName = inputType.ToDisplayString(FullyQualifiedFormat),
                Name = name,
                SourceValue = 0, // Route
                Required = true,
                IsEnum = inputType.TypeKind == TypeKind.Enum
            });
        }

        // Auto-extract route tokens for Route+Body convention: when Body is
        // specified without an explicit Input, extract route tokens and create
        // route bindings with the type inferred from the matching body DTO property.
        // The token name is kept as the original route value key (e.g., "id");
        // the binding emitter PascalCases it for DTO property access.
        if (bodyType is not null && inputType is null)
        {
            var routeTokens = ExtractAllRouteTokenNames(route);
            foreach (var token in routeTokens)
            {
                // PascalCase the token to match DTO property name
                var pascalName = token.Length > 0
                    ? char.ToUpperInvariant(token[0]) + token.Substring(1)
                    : token;

                // Look up the property type on the Body type
                var propertyType = FindPropertyTypeOnType(bodyType, pascalName);
                var typeName = propertyType?.ToDisplayString(FullyQualifiedFormat)
                    ?? "global::System.String"; // fallback to string if property not found

                builder.Add(new CapabilityEndpointInputRecord
                {
                    TypeName = typeName,
                    Name = token,
                    SourceValue = 0, // Route
                    Required = true,
                    IsEnum = propertyType is not null && propertyType.TypeKind == TypeKind.Enum
                });
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Extracts the first {token} name from a route template like "items/{id}/details".
    /// Returns null if no token is found.
    /// </summary>
    internal static string? ExtractFirstRouteTokenName(string route)
    {
        if (string.IsNullOrEmpty(route))
            return null;

        foreach (var segment in route.Split('/'))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                var token = trimmed.Substring(1, trimmed.Length - 2);
                NormalizeRouteToken(ref token);
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts all {token} names from a route template like "items/{id}/sub/{subId}".
    /// </summary>
    internal static ImmutableArray<string> ExtractAllRouteTokenNames(string route)
    {
        if (string.IsNullOrEmpty(route))
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var segment in route.Split('/'))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                var token = trimmed.Substring(1, trimmed.Length - 2);
                NormalizeRouteToken(ref token);
                builder.Add(token);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Normalizes a raw route token extracted from {token} braces by stripping
    /// constraint (:...), catch-all (**) prefix, and optional (?) suffix.
    /// E.g. "{id:int}" → "id", "{**id?}" → "id".
    /// This mirrors the runtime behavior in <see cref="CapabilityEndpointDescriptorValidator.ExtractRouteTokens"/>.
    /// </summary>
    private static void NormalizeRouteToken(ref string token)
    {
        // Strip constraint (everything after ':')
        var constraintIndex = token.IndexOf(':');
        if (constraintIndex >= 0)
            token = token.Substring(0, constraintIndex);

        // Strip catch-all prefix
        if (token.StartsWith("**", StringComparison.Ordinal))
            token = token.Substring(2);

        // Strip optional suffix
        if (token.EndsWith("?", StringComparison.Ordinal))
            token = token.Substring(0, token.Length - 1);
    }

    /// <summary>
    /// Looks up a public property by name on a type symbol and returns its type,
    /// or null if not found.
    /// </summary>
    internal static ITypeSymbol? FindPropertyTypeOnType(INamedTypeSymbol typeSymbol, string propertyName)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            // Skip System.Object
            if (current.SpecialType == SpecialType.System_Object)
                break;

            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol
                    {
                        Name: var name,
                        DeclaredAccessibility: Accessibility.Public,
                        IsStatic: false
                    }
                    && string.Equals(name, propertyName, StringComparison.Ordinal))
                {
                    return ((IPropertySymbol)member).Type;
                }
            }

            current = current.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is a known scalar type or enum,
    /// i.e. a type that can be parsed from a route/query string value.
    /// </summary>
    internal static bool IsSupportedRouteBindingType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            // Enums are supported
            if (namedType.TypeKind == TypeKind.Enum)
                return true;

            // Standard scalar types detectable via SpecialType
            if (namedType.SpecialType is SpecialType.System_String
                or SpecialType.System_Int32 or SpecialType.System_Int64
                or SpecialType.System_Boolean or SpecialType.System_Double
                or SpecialType.System_Single or SpecialType.System_Decimal
                or SpecialType.System_DateTime or SpecialType.System_Int16
                or SpecialType.System_UInt16 or SpecialType.System_UInt32
                or SpecialType.System_UInt64 or SpecialType.System_Byte
                or SpecialType.System_SByte)
            {
                return true;
            }

            // Types whose SpecialType is None — check by name
            if (namedType.SpecialType == SpecialType.None)
            {
                var fullName = namedType.ToDisplayString();
                if (fullName is "System.Guid" or "global::System.Guid"
                    or "System.DateTimeOffset" or "global::System.DateTimeOffset")
                {
                    return true;
                }
            }
        }

        return false;
    }

    // --- Helpers (mirror existing pattern in CapabilityEndpointGenerator) ---

    private static int GetNamedIntArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value is { IsNull: false } && kvp.Value.Value is int intValue)
                    return intValue;
            }
        }

        return 0;
    }

    private static string? GetNamedStringArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value.IsNull)
                    return null;
                return kvp.Value.Value as string;
            }
        }

        return null;
    }

    private static ImmutableArray<string> GetNamedStringArrayArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value.IsNull || kvp.Value.Kind == TypedConstantKind.Array)
                {
                    var values = kvp.Value.Values;
                    if (values.IsDefaultOrEmpty)
                        return ImmutableArray<string>.Empty;

                    var builder = ImmutableArray.CreateBuilder<string>(values.Length);
                    foreach (var val in values)
                    {
                        if (val.Value is string str)
                            builder.Add(str);
                    }

                    return builder.ToImmutable();
                }
            }
        }

        return ImmutableArray<string>.Empty;
    }

    private static bool GetNamedBoolArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value is { IsNull: false } && kvp.Value.Value is bool boolValue)
                    return boolValue;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? GetNamedTypeArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value is { IsNull: false, Kind: TypedConstantKind.Type })
                {
                    return kvp.Value.Value as INamedTypeSymbol;
                }
            }
        }

        return null;
    }
}
