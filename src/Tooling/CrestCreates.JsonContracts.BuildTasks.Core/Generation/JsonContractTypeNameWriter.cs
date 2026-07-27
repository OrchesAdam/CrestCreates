using CrestCreates.JsonContracts.BuildTasks.Semantic;
using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Generation;

public static class JsonContractTypeNameWriter
{
    private static readonly SymbolDisplayFormat s_canonicalFormat =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable
                | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);

    public static string WriteCanonicalName(ITypeSymbol type)
    {
        var normalized = JsonContractRootNormalizer.Normalize(type);
        return ((ISymbol)normalized).ToDisplayString(s_canonicalFormat);
    }

    public static string WriteTypeofExpression(ITypeSymbol type)
    {
        var canonicalName = WriteCanonicalName(type);
        return $"typeof({canonicalName})";
    }
}
