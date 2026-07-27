using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

internal static class JsonContractRootValidator
{
    private static readonly SymbolDisplayFormat s_format =
        new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);

    public static JsonContractDiagnostic? Validate(
        ITypeSymbol root,
        JsonContractRootOrigin origin,
        string contextMetadataName)
    {
        var failure = FindFailure(root);
        if (failure is null)
            return null;

        var diagnostic = new JsonContractDiagnostic
        {
            Id = failure.Value.Id,
            Message = $"Root type '{root.ToDisplayString()}' from {origin.SourceKind} {failure.Value.Message}",
            Severity = JsonContractDiagnosticSeverity.Error,
            ContextMetadataName = contextMetadataName,
            SurfaceMetadataName = origin.DeclaringSurface,
            MethodSignature = origin.MemberSignature,
            ParameterName = origin.RoleName,
            OffendingType = root.ToDisplayString(s_format),
        };

        return diagnostic.WithLocation(origin.Location);
    }

    private static (string Id, string Message)? FindFailure(ITypeSymbol type)
    {
        if (type is IErrorTypeSymbol)
        {
            return (
                JsonContractDiagnosticIds.UnresolvedPreCoreCompileRoot,
                "is unresolved. Move the contract to a referenced assembly or an earlier MSBuild compile source.");
        }

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
        {
            return (
                JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter,
                "is a pointer or function pointer and is not supported.");
        }

        if (type is IArrayTypeSymbol array)
            return FindFailure(array.ElementType);

        if (type is ITypeParameterSymbol)
        {
            return (
                JsonContractDiagnosticIds.InvalidRoot,
                "is an open generic. Only closed generic roots are supported.");
        }

        if (type is not INamedTypeSymbol namedType)
            return null;

        if (namedType.IsUnboundGenericType || namedType.TypeArguments.Any(ContainsTypeParameter))
        {
            return (
                JsonContractDiagnosticIds.InvalidRoot,
                "is an open generic. Only closed generic roots are supported.");
        }

        if (namedType.IsRefLikeType)
        {
            return (
                JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter,
                "is ref-like and is not supported.");
        }

        for (var current = namedType; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return (
                    JsonContractDiagnosticIds.InaccessibleRoot,
                    $"is not accessible (accessibility: {current.DeclaredAccessibility}).");
            }
        }

        foreach (var typeArgument in namedType.TypeArguments)
        {
            var typeArgumentFailure = FindFailure(typeArgument);
            if (typeArgumentFailure is not null)
                return typeArgumentFailure;
        }

        return null;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
        => type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
            INamedTypeSymbol named => named.IsUnboundGenericType || named.TypeArguments.Any(ContainsTypeParameter),
            _ => false,
        };
}
