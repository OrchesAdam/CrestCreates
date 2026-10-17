#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.Tooling.HttpProjection;

/// <summary>
/// Issue #129 S2: builds <see cref="AppServiceHttpProjectionModel"/> from a real declaration. The
/// anchor carries <c>[CapabilityCompatibilityProjection(ContractType = ...)]</c>; only the contract
/// and its inherited interfaces contribute actions. Diagnostics are the frozen CCHTTP001/003/008
/// family; nothing here falls back to reflection or to the anchor's own public methods.
/// </summary>
public static class AppServiceHttpProjectionModelBuilder
{
    private const string ProjectionAttributeName =
        "CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjectionAttribute";

    private const string ActionAttributeName =
        "CrestCreates.Domain.Shared.Attributes.AppServiceHttpActionAttribute";

    private static readonly SymbolDisplayFormat Display = SymbolDisplayFormat.FullyQualifiedFormat;

    public static AppServiceHttpProjectionBuildResult Build(CSharpCompilation compilation, INamedTypeSymbol anchor)
    {
        var diagnostics = new List<AppServiceHttpProjectionDiagnostic>();
        var projection = FindAttribute(anchor, ProjectionAttributeName);
        if (projection is null)
        {
            return Failed(diagnostics, AppServiceHttpDiagnosticCodes.UnsupportedDeclaration,
                "the projection anchor must carry [CapabilityCompatibilityProjection]", anchor);
        }

        var registrationMode = ReadEnum(projection, "RegistrationMode", 0) == 1
            ? AppServiceHttpRegistrationMode.LegacyStatic
            : AppServiceHttpRegistrationMode.ExplicitHost;

        if (registrationMode == AppServiceHttpRegistrationMode.LegacyStatic)
        {
            // The legacy static front-end owns this declaration; the formal model does not apply,
            // and declining ownership is not a diagnostic.
            return new AppServiceHttpProjectionBuildResult(null, diagnostics);
        }

        var contractType = ReadType(projection, "ContractType");
        if (contractType is null)
        {
            return Failed(diagnostics, AppServiceHttpDiagnosticCodes.UnsupportedDeclaration,
                "an explicit-host projection must declare ContractType; the anchor's own methods are never scanned",
                anchor);
        }

        if (contractType is not INamedTypeSymbol contractSymbol
            || contractType.TypeKind != TypeKind.Interface
            || contractSymbol.TypeParameters.Length > 0)
        {
            return Failed(diagnostics, AppServiceHttpDiagnosticCodes.UnsupportedDeclaration,
                $"ContractType '{contractType.ToDisplayString()}' must be a closed interface", anchor);
        }

        if (!compilation.IsSymbolAccessibleWithin(contractType, compilation.Assembly))
        {
            return Failed(diagnostics, AppServiceHttpDiagnosticCodes.UnsupportedDeclaration,
                $"ContractType '{contractType.ToDisplayString()}' is not accessible from the HTTP owner assembly", anchor);
        }

        var contract = contractSymbol;
        var serviceName = AppServiceHttpWireConvention.ServiceName(contract.Name);
        var routePrefix = (ReadString(projection, "RoutePrefix") ?? AppServiceHttpWireConvention.ToKebabCase(serviceName))
            .Trim('/');
        var capabilityPrefix = (ReadString(projection, "CapabilityIdPrefix")
                                ?? AppServiceHttpWireConvention.DefaultCapabilityPrefix + AppServiceHttpWireConvention.ToKebabCase(serviceName))
            .TrimEnd('.');

        var ownerAssembly = compilation.AssemblyName ?? string.Empty;
        var actions = new List<AppServiceHttpActionModel>();
        var referenced = new List<AppServiceHttpReferenceIdentity>();
        AddReference(referenced, contract, ownerAssembly, isContract: true);

        foreach (var contractMethod in EnumerateContractMethods(contract))
        {
            var action = BuildAction(compilation, contract, anchor, contractMethod, capabilityPrefix, diagnostics, referenced);
            if (action is not null)
            {
                actions.Add(action);
            }
        }

        if (actions.Count == 0)
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.UnsupportedDeclaration,
                "the contract exposes no ordinary methods to project",
                AppServiceHttpDiagnosticSeverity.Error,
                anchor.ToDisplayString()));
        }

        foreach (var duplicate in actions
                     .GroupBy(action => action.HttpMethod + " " + action.RoutePattern, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.OwnerRouteActionConflict,
                $"two actions publish the same method/route '{duplicate.Key}'",
                AppServiceHttpDiagnosticSeverity.Error,
                anchor.ToDisplayString()));
        }

        foreach (var duplicate in actions
                     .GroupBy(action => action.CapabilityId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.OwnerRouteActionConflict,
                $"two actions resolve to the same capability id '{duplicate.Key}'",
                AppServiceHttpDiagnosticSeverity.Error,
                anchor.ToDisplayString()));
        }

        var model = new AppServiceHttpProjectionModel
        {
            HttpOwnerAssemblyName = ownerAssembly,
            AnchorTypeFullName = anchor.ToDisplayString(Display),
            ContractAssemblyName = contract.ContainingAssembly?.Name ?? string.Empty,
            ContractFullName = contract.ToDisplayString(Display),
            ContractNamespace = contract.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            ContractName = contract.Name,
            RoutePrefix = routePrefix,
            CapabilityIdPrefix = capabilityPrefix,
            RegistrationMode = registrationMode,
            Actions = actions,
            ReferencedTypes = referenced
                .GroupBy(reference => reference.AssemblyName + "|" + reference.FullMetadataName, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(reference => reference.FullMetadataName, StringComparer.Ordinal)
                .ToList(),
            DeclarationLocation = DescribeLocation(anchor),
        };

        return new AppServiceHttpProjectionBuildResult(model, diagnostics);
    }

    private static AppServiceHttpActionModel? BuildAction(
        CSharpCompilation compilation,
        INamedTypeSymbol contract,
        INamedTypeSymbol anchor,
        IMethodSymbol contractMethod,
        string capabilityPrefix,
        List<AppServiceHttpProjectionDiagnostic> diagnostics,
        List<AppServiceHttpReferenceIdentity> referenced)
    {
        var actionName = contractMethod.Name;
        var contractAttribute = FindAttribute(contractMethod, ActionAttributeName);
        var implementation = anchor.FindImplementationForInterfaceMember(contractMethod) as IMethodSymbol;
        var implementationAttribute = implementation is null ? null : FindAttribute(implementation, ActionAttributeName);

        if (contractAttribute is not null && implementationAttribute is not null
            && !SameNamedArguments(contractAttribute, implementationAttribute))
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.ActionDeclarationContradiction,
                $"action '{actionName}' is declared on both the contract and the implementation with different values",
                AppServiceHttpDiagnosticSeverity.Error,
                contractMethod.ToDisplayString()));
        }

        var declaration = contractAttribute ?? implementationAttribute;

        var parameters = contractMethod.Parameters.Where(parameter => !IsCancellationToken(parameter.Type)).ToList();
        var cancellationToken = contractMethod.Parameters.FirstOrDefault(parameter => IsCancellationToken(parameter.Type));
        if (cancellationToken is null)
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.ActionDeclarationContradiction,
                $"action '{actionName}' must accept an optional CancellationToken so the request/kernel token can propagate",
                AppServiceHttpDiagnosticSeverity.Warning,
                contractMethod.ToDisplayString()));
        }

        var scalarParameters = parameters.Where(parameter => IsScalar(parameter.Type)).ToList();
        var complexParameters = parameters.Where(parameter => !IsScalar(parameter.Type)).ToList();
        if (complexParameters.Count > 1)
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.UnsupportedDeclaration,
                $"action '{actionName}' binds more than one body parameter",
                AppServiceHttpDiagnosticSeverity.Error,
                contractMethod.ToDisplayString()));
            return null;
        }

        var explicitVerb = ReadEnum(declaration, "Verb", 0);
        var httpMethod = explicitVerb switch
        {
            1 => "GET",
            2 => "POST",
            3 => "PUT",
            4 => "PATCH",
            5 => "DELETE",
            _ => AppServiceHttpWireConvention.DefaultHttpMethod(actionName),
        };

        var scalarNames = scalarParameters.Select(parameter => parameter.Name).ToList();
        var routeTemplate = (ReadString(declaration, "Route")
                             ?? AppServiceHttpWireConvention.DefaultRoute(actionName, scalarNames)).Trim('/');

        var routePlaceholders = ReadRoutePlaceholders(routeTemplate);
        var routeParameters = scalarParameters.Where(parameter => routePlaceholders.Contains(parameter.Name)).ToList();
        var queryParameters = scalarParameters.Where(parameter => !routePlaceholders.Contains(parameter.Name)).ToList();
        foreach (var placeholder in routePlaceholders.Where(placeholder => scalarParameters.All(parameter => parameter.Name != placeholder)))
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.UnsupportedDeclaration,
                $"route '{routeTemplate}' declares '{{{placeholder}}}' but action '{actionName}' has no matching scalar parameter",
                AppServiceHttpDiagnosticSeverity.Error,
                contractMethod.ToDisplayString()));
        }

        var bodyParameter = complexParameters.SingleOrDefault();
        var explicitBody = ReadEnum(declaration, "Body", 0);
        var bodyPolicy = explicitBody switch
        {
            1 => AppServiceHttpBodyPolicy.None,
            2 => AppServiceHttpBodyPolicy.Required,
            3 => AppServiceHttpBodyPolicy.Optional,
            _ => bodyParameter is null ? AppServiceHttpBodyPolicy.None : AppServiceHttpBodyPolicy.Required,
        };

        if (bodyPolicy == AppServiceHttpBodyPolicy.None && bodyParameter is not null)
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.ActionDeclarationContradiction,
                $"action '{actionName}' declares Body = None but binds a body parameter",
                AppServiceHttpDiagnosticSeverity.Error,
                contractMethod.ToDisplayString()));
        }

        var kind = ReadEnum(declaration, "Kind", 0) switch
        {
            1 => AppServiceHttpOperationKind.Query,
            2 => AppServiceHttpOperationKind.Command,
            _ => httpMethod is "POST" or "PUT" or "PATCH" or "DELETE"
                ? AppServiceHttpOperationKind.Command
                : AppServiceHttpOperationKind.Query,
        };

        var requiresConcurrencyToken = ReadBool(declaration, "RequiresConcurrencyToken");
        if (requiresConcurrencyToken && routeParameters.Count == 0)
        {
            diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
                AppServiceHttpDiagnosticCodes.ActionDeclarationContradiction,
                $"action '{actionName}' requires a concurrency token but has no route identity",
                AppServiceHttpDiagnosticSeverity.Warning,
                contractMethod.ToDisplayString()));
        }

        var permissions = ReadStringArray(declaration, "Permissions");
        var resultType = UnwrapResult(contractMethod.ReturnType);
        var needsEnvelope = bodyParameter is not null && scalarParameters.Count > 0;

        var scalarInputs = scalarParameters
            .Select(parameter => new AppServiceHttpInputParameter
            {
                Name = parameter.Name,
                TypeFullName = parameter.Type.ToDisplayString(Display),
                Binding = routePlaceholders.Contains(parameter.Name) ? "route" : "query",
            })
            .ToList();

        foreach (var parameter in parameters)
        {
            AddReference(referenced, parameter.Type, compilation.AssemblyName ?? string.Empty, isContract: false);
        }

        if (resultType is not null)
        {
            AddReference(referenced, resultType, compilation.AssemblyName ?? string.Empty, isContract: false);
        }

        var capabilityId = capabilityPrefix + "." + AppServiceHttpWireConvention.ToKebabCase(AppServiceHttpWireConvention.TrimAsyncSuffix(actionName));
        return new AppServiceHttpActionModel
        {
            Name = actionName,
            HttpMethod = httpMethod,
            RoutePattern = routeTemplate,
            OperationKind = kind,
            BodyPolicy = bodyPolicy,
            Permissions = permissions,
            CapabilityId = capabilityId,
            EndpointId = AppServiceHttpWireConvention.EndpointId(capabilityId),
            ClosedContractMethodSignature = ClosedSignature(contractMethod),
            CancellationTokenParameterName = cancellationToken?.Name,
            BodyTypeFullName = bodyParameter?.Type.ToDisplayString(Display),
            ResultTypeFullName = resultType?.ToDisplayString(Display),
            RouteIdentityParameterName = routeParameters.FirstOrDefault()?.Name,
            ScalarInputs = scalarInputs,
            InputEnvelopeFullName = needsEnvelope
                ? AppServiceHttpWireConvention.EnvelopeFullName(contract.Name, actionName)
                : null,
            RequiresConcurrencyToken = requiresConcurrencyToken,
        };
    }

    private static IEnumerable<IMethodSymbol> EnumerateContractMethods(INamedTypeSymbol contract)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in new[] { contract }.Concat(contract.AllInterfaces))
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || method.IsStatic)
                {
                    continue;
                }

                var signature = ClosedSignature(method);
                if (seen.Add(signature))
                {
                    yield return method;
                }
            }
        }
    }

    private static string ClosedSignature(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(parameter =>
            parameter.Type.ToDisplayString(Display) + " " + parameter.Name));
        return method.ReturnType.ToDisplayString(Display) + " " + method.Name + "(" + parameters + ")";
    }

    private static ITypeSymbol? UnwrapResult(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return null;
        }

        if (returnType is INamedTypeSymbol named)
        {
            if (named.IsGenericType
                && named.ConstructedFrom.ToDisplayString() is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>")
            {
                return named.TypeArguments[0];
            }

            if (named.Name is "Task" or "ValueTask"
                && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
            {
                return null;
            }
        }

        return returnType;
    }

    internal static bool IsScalar(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return IsScalar(nullable.TypeArguments[0]);
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
            or SpecialType.System_SByte or SpecialType.System_Byte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_Decimal or SpecialType.System_String)
        {
            return true;
        }

        return type.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset"
            or "System.TimeSpan" or "System.DateOnly" or "System.TimeOnly";
    }

    private static bool IsCancellationToken(ITypeSymbol type)
        => type.ToDisplayString() == "System.Threading.CancellationToken";

    private static HashSet<string> ReadRoutePlaceholders(string route)
    {
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        while (index < route.Length)
        {
            var open = route.IndexOf('{', index);
            if (open < 0)
            {
                break;
            }

            var close = route.IndexOf('}', open + 1);
            if (close < 0)
            {
                break;
            }

            var name = route.Substring(open + 1, close - open - 1).Split(':')[0].Trim();
            if (name.Length > 0)
            {
                placeholders.Add(name);
            }

            index = close + 1;
        }

        return placeholders;
    }

    private static void AddReference(
        List<AppServiceHttpReferenceIdentity> references,
        ITypeSymbol type,
        string ownerAssemblyName,
        bool isContract)
    {
        if (type is IArrayTypeSymbol array)
        {
            AddReference(references, array.ElementType, ownerAssemblyName, isContract);
            return;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                AddReference(references, argument, ownerAssemblyName, isContract: false);
            }

            var assembly = named.ContainingAssembly?.Name;
            if (named.TypeKind != TypeKind.Error
                && !string.IsNullOrEmpty(assembly)
                && !string.Equals(assembly, ownerAssemblyName, StringComparison.Ordinal)
                && !IsPlatformAssembly(assembly!))
            {
                references.Add(new AppServiceHttpReferenceIdentity
                {
                    AssemblyName = assembly!,
                    FullMetadataName = named.ToDisplayString(Display).Replace("global::", string.Empty),
                    IsContract = isContract,
                });
            }
        }
    }

    private static bool IsPlatformAssembly(string assemblyName)
        => assemblyName.StartsWith("System", StringComparison.Ordinal)
           || assemblyName.StartsWith("Microsoft", StringComparison.Ordinal)
           || assemblyName is "netstandard" or "mscorlib";

    private static AttributeData? FindAttribute(ISymbol symbol, string attributeFullName)
        => symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == attributeFullName);

    private static ITypeSymbol? ReadType(AttributeData attribute, string name)
        => attribute.NamedArguments
            .Where(argument => argument.Key == name && argument.Value.Kind == TypedConstantKind.Type)
            .Select(argument => argument.Value.Value as ITypeSymbol)
            .FirstOrDefault();

    private static string? ReadString(AttributeData? attribute, string name)
        => attribute?.NamedArguments
            .Where(argument => argument.Key == name && argument.Value.Value is string)
            .Select(argument => argument.Value.Value as string)
            .FirstOrDefault();

    private static int ReadEnum(AttributeData? attribute, string name, int fallback)
    {
        var constant = attribute?.NamedArguments
            .Where(argument => argument.Key == name)
            .Select(argument => argument.Value)
            .FirstOrDefault();
        if (constant is not { Kind: TypedConstantKind.Enum or TypedConstantKind.Primitive }
            || constant.Value.Value is null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToInt32(constant.Value.Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return fallback;
        }
    }

    private static bool ReadBool(AttributeData? attribute, string name)
    {
        var values = attribute?.NamedArguments
            .Where(argument => argument.Key == name && argument.Value.Value is bool)
            .Select(argument => (bool)argument.Value.Value!)
            .ToList();
        return values is { Count: > 0 } && values[0];
    }

    private static IReadOnlyList<string> ReadStringArray(AttributeData? attribute, string name)
    {
        var constant = attribute?.NamedArguments
            .Where(argument => argument.Key == name)
            .Select(argument => argument.Value)
            .FirstOrDefault();
        if (constant is not { Kind: TypedConstantKind.Array } array)
        {
            return Array.Empty<string>();
        }

        return array.Values.Select(value => value.Value as string ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private static bool SameNamedArguments(AttributeData left, AttributeData right)
    {
        var leftValues = left.NamedArguments.Select(argument => argument.Key + "=" + Render(argument.Value)).OrderBy(value => value, StringComparer.Ordinal);
        var rightValues = right.NamedArguments.Select(argument => argument.Key + "=" + Render(argument.Value)).OrderBy(value => value, StringComparer.Ordinal);
        return leftValues.SequenceEqual(rightValues, StringComparer.Ordinal);
    }

    private static string Render(TypedConstant constant)
        => constant.Kind switch
        {
            TypedConstantKind.Array => string.Join(",", constant.Values.Select(Render)),
            TypedConstantKind.Type => (constant.Value as ITypeSymbol)?.ToDisplayString(Display) ?? "?",
            _ => constant.Value?.ToString() ?? "null",
        };

    private static string DescribeLocation(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is null)
        {
            return "<metadata>";
        }

        var lineSpan = location.GetLineSpan();
        return lineSpan.Path + ":" + (lineSpan.StartLinePosition.Line + 1) + ":" + (lineSpan.StartLinePosition.Character + 1);
    }

    private static AppServiceHttpProjectionBuildResult Failed(
        List<AppServiceHttpProjectionDiagnostic> diagnostics,
        string id,
        string message,
        ISymbol member)
    {
        diagnostics.Add(new AppServiceHttpProjectionDiagnostic(
            id, message, AppServiceHttpDiagnosticSeverity.Error, member.ToDisplayString()));
        return new AppServiceHttpProjectionBuildResult(null, diagnostics);
    }
}
