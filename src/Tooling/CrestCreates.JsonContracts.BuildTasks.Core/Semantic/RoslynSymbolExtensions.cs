using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

public static class RoslynSymbolExtensions
{
    public static bool DerivesFrom(this INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    public static bool IsPartial(this INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax typeDecl)
            {
                foreach (var mod in typeDecl.Modifiers)
                {
                    if (mod.IsKind(SyntaxKind.PartialKeyword))
                        return true;
                }
            }
        }
        return false;
    }

    public static IEnumerable<INamedTypeSymbol> GetAllTypes(this INamespaceSymbol namespaceSymbol)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                foreach (var type in childNs.GetAllTypes())
                    yield return type;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in type.GetAllNestedTypes())
                    yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNestedTypes(this INamedTypeSymbol type)
    {
        foreach (var member in type.GetTypeMembers())
        {
            yield return member;
            foreach (var nested in member.GetAllNestedTypes())
                yield return nested;
        }
    }
}
