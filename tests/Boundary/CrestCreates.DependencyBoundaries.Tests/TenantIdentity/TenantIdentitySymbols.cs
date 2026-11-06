using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Shared symbol helpers for the TenantId guard: compilation inputs, port classification and
/// authoritative-source shape checks.
/// </summary>
internal static class TenantIdentitySymbols
{
    public static IReadOnlyList<SyntaxTree> GlobalUsings(TenantProjectInput project)
    {
        if (!string.Equals(project.ImplicitUsings, "enable", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var names = new List<string>
        {
            "System",
            "System.Collections.Generic",
            "System.IO",
            "System.Linq",
            "System.Net.Http",
            "System.Threading",
            "System.Threading.Tasks",
        };

        if (project.IsWebSdk)
        {
            names.AddRange([
                "System.Net.Http.Json",
                "Microsoft.AspNetCore.Builder",
                "Microsoft.AspNetCore.Hosting",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Routing",
                "Microsoft.Extensions.DependencyInjection",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.Logging",
            ]);
        }

        var text = string.Join(Environment.NewLine, names.Select(name => $"global using {name};"));
        return [CSharpSyntaxTree.ParseText(SourceText.From(text), ParseOptions(project), path: "ImplicitUsings.g.cs")];
    }

    /// <summary>
    /// Parse options must carry the project's real preprocessor symbols: otherwise every <c>#if DEBUG</c>
    /// block is inactive trivia and the code the build actually compiles is never analysed.
    /// </summary>
    public static CSharpParseOptions ParseOptions(TenantProjectInput project) =>
        new(
            LanguageVersion.Preview,
            preprocessorSymbols: (project.DefineConstants ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static CSharpCompilationOptions Options(TenantProjectInput project) =>
        new(
            string.Equals(project.OutputType, "Exe", StringComparison.OrdinalIgnoreCase)
                ? OutputKind.ConsoleApplication
                : OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: string.Equals(project.Nullable, "disable", StringComparison.OrdinalIgnoreCase)
                ? NullableContextOptions.Disable
                : NullableContextOptions.Enable,
            allowUnsafe: true);

    /// <summary>
    /// Maps an invocation argument to a tenant identity slot. Explicit ports win; otherwise the generic net
    /// catches any CrestCreates-declared parameter whose name denotes the tenant id, so a new API cannot
    /// silently become an unguarded identity exit.
    /// </summary>
    public static (TenantIdentityRule Rule, string Label)? ClassifyInvocationSlot(IMethodSymbol target, IParameterSymbol parameter)
    {
        var declaringTypes = EnumerateTypeNames(target.ContainingType).ToHashSet(StringComparer.Ordinal);

        foreach (var port in TenantIdentityPorts.InvocationPorts)
        {
            if (string.Equals(port.MethodName, target.Name, StringComparison.Ordinal)
                && declaringTypes.Contains(port.DeclaringTypeFullName)
                && parameter.Ordinal == port.Slot)
            {
                return (port.Rule, port.Label);
            }
        }

        if (!TenantIdentityPorts.IsIdentityMemberName(parameter.Name))
        {
            return null;
        }

        var assemblyName = target.ContainingAssembly?.Name ?? string.Empty;
        var isOwnContract = assemblyName.StartsWith("CrestCreates", StringComparison.OrdinalIgnoreCase)
            || IsSourceDeclared(target);
        if (!isOwnContract)
        {
            return null;
        }

        var @namespace = target.ContainingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var rule = @namespace.Contains("Caching", StringComparison.Ordinal)
            || @namespace.Contains("Authorization", StringComparison.Ordinal)
            || @namespace.Contains("DataFilter", StringComparison.Ordinal)
            || @namespace.Contains("Security", StringComparison.Ordinal)
            || @namespace.Contains("Data", StringComparison.Ordinal)
            || @namespace.Contains("MultiTenancy", StringComparison.Ordinal) && target.Name.Contains("Context", StringComparison.Ordinal)
                ? TenantIdentityRule.TI002
                : TenantIdentityRule.TI001;

        return (rule, $"{FullName(target.ContainingType)}.{target.Name}({parameter.Name}) identity slot");
    }

    public static bool IsSourceDeclared(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences.Length > 0
            && symbol.ContainingAssembly?.Name?.StartsWith("CrestCreates", StringComparison.OrdinalIgnoreCase) != false;

    public static IEnumerable<string> EnumerateTypeNames(INamedTypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<INamedTypeSymbol>();
        pending.Push(type);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var name = FullName(current);
            if (!seen.Add(name))
            {
                continue;
            }

            yield return name;

            foreach (var @interface in current.AllInterfaces)
            {
                pending.Push(@interface);
            }

            if (current.OriginalDefinition is not null && !ReferenceEquals(current.OriginalDefinition, current))
            {
                pending.Push(current.OriginalDefinition);
            }

            if (current.BaseType is not null && current.BaseType.SpecialType == SpecialType.None)
            {
                pending.Push(current.BaseType);
            }
        }
    }

    public static bool ImplementsTenantInfo(INamedTypeSymbol type) =>
        FullName(type) == TenantIdentityPorts.TenantInfoType
            || type.AllInterfaces.Any(@interface => FullName(@interface) == TenantIdentityPorts.TenantInfoType);

    public static bool IsIdentityConstructorParameter(IParameterSymbol parameter)
    {
        var normalized = TenantIdentityPorts.Normalize(parameter.Name);
        return normalized == "id"
            || normalized == "tenantid"
            || normalized.EndsWith("tenantid", StringComparison.Ordinal);
    }

    /// <summary>
    /// Identity members are classified by declaration, not by the spelling at the call site: entity tenant
    /// filters (<c>IMultiTenant.TenantId</c>) and the id member of <c>ITenantInfo</c> implementers both count,
    /// and object-initializer assignments resolve through the bound property reference.
    /// </summary>
    public static bool IsTenantIdMember(SemanticModel model, ExpressionSyntax expression)
    {
        var symbol = model.GetSymbolInfo(expression).Symbol as IPropertySymbol
            ?? (model.GetOperation(expression) as Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)?.Property;
        return symbol is not null
            && (TenantIdentityPorts.IsIdentityMemberName(symbol.Name)
                || IsMultiTenantIdProperty(symbol)
                || IsTenantInfoIdProperty(symbol));
    }

    public static bool IsTenantInfoIdExpression(SemanticModel model, ExpressionSyntax expression)
    {
        var symbol = model.GetSymbolInfo(expression).Symbol as IPropertySymbol
            ?? (model.GetOperation(expression) as Microsoft.CodeAnalysis.Operations.IPropertyReferenceOperation)?.Property;
        return symbol is not null && IsTenantInfoIdProperty(symbol);
    }

    public static bool IsMultiTenantIdProperty(IPropertySymbol property) =>
        property.Name == "TenantId"
            && property.ContainingType.AllInterfaces.Any(@interface =>
                FullName(@interface) == TenantIdentityPorts.MultiTenantEntityType);

    public static bool IsTenantInfoIdProperty(IPropertySymbol property) =>
        property.Name == "Id"
            && (FullName(property.ContainingType) == TenantIdentityPorts.TenantInfoType
                || property.ContainingType.AllInterfaces.Any(@interface =>
                    FullName(@interface) == TenantIdentityPorts.TenantInfoType));

    private static readonly SymbolDisplayFormat TypeNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string FullName(ISymbol symbol) => symbol switch
    {
        ITypeSymbol type => type.ToDisplayString(TypeNameFormat),
        ISymbol member when member.ContainingType is not null =>
            $"{member.ContainingType.ToDisplayString(TypeNameFormat)}.{member.Name}",
        _ => symbol.Name,
    };

    /// <summary>
    /// Names that a value-providing expression can ultimately be derived from, ignoring value-preserving
    /// string conversions. Used to check that a locked main-chain exit really reads an id source.
    /// </summary>
    public static IReadOnlyList<string> TerminalValueNames(SemanticModel model, ExpressionSyntax expression)
    {
        var names = new List<string>();
        Collect(model, Unwrap(expression), names);
        return names;

        void Collect(SemanticModel semanticModel, ExpressionSyntax node, List<string> sink)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    var symbol = semanticModel.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol;
                    var name = symbol?.Name ?? InvokedName(invocation.Expression);
                    if (symbol is not null && IsValuePreserving(symbol) && invocation.Expression is MemberAccessExpressionSyntax access)
                    {
                        Collect(semanticModel, access.Expression, sink);
                        return;
                    }

                    sink.Add(name);
                    return;
                case MemberAccessExpressionSyntax memberAccess:
                    var member = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                    if (member is { IsStatic: true, ContainingType.SpecialType: SpecialType.System_String })
                    {
                        // string.Empty is an empty identity value, not a competing source.
                        return;
                    }

                    if (semanticModel.GetConstantValue(memberAccess).HasValue)
                    {
                        return;
                    }

                    sink.Add(member?.Name ?? memberAccess.Name.Identifier.ValueText);
                    return;
                case MemberBindingExpressionSyntax binding:
                    sink.Add(binding.Name.Identifier.ValueText);
                    return;
                case SimpleNameSyntax simple:
                    sink.Add(semanticModel.GetSymbolInfo(simple).Symbol?.Name ?? simple.Identifier.ValueText);
                    return;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
                    Collect(semanticModel, Unwrap(binary.Left), sink);
                    Collect(semanticModel, Unwrap(binary.Right), sink);
                    return;
                case ConditionalExpressionSyntax conditional:
                    Collect(semanticModel, Unwrap(conditional.WhenTrue), sink);
                    Collect(semanticModel, Unwrap(conditional.WhenFalse), sink);
                    return;
                case ConditionalAccessExpressionSyntax conditionalAccess:
                    Collect(semanticModel, Unwrap(conditionalAccess.WhenNotNull), sink);
                    return;
                case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    Collect(semanticModel, Unwrap(postfix.Operand), sink);
                    return;
                case ParenthesizedExpressionSyntax parenthesized:
                    Collect(semanticModel, Unwrap(parenthesized.Expression), sink);
                    return;
                case CastExpressionSyntax cast:
                    Collect(semanticModel, Unwrap(cast.Expression), sink);
                    return;
                default:
                    sink.Add(node.ToString());
                    return;
            }
        }
    }

    private static bool IsValuePreserving(IMethodSymbol symbol) =>
        symbol.Name is "ToString" or "Trim" or "TrimStart" or "TrimEnd" or "ToLower" or "ToLowerInvariant"
            or "ToUpper" or "ToUpperInvariant" or "AsSpan";

    private static string InvokedName(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => string.Empty,
    };

    public static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
        CastExpressionSyntax cast => Unwrap(cast.Expression),
        AwaitExpressionSyntax awaited => Unwrap(awaited.Expression),
        PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) => Unwrap(postfix.Operand),
        _ => expression,
    };

    /// <summary>
    /// True when every terminal name of an expression denotes the authoritative tenant id.
    /// </summary>
    public static bool IsAuthoritativeIdShape(IReadOnlyList<string> terminalNames)
    {
        if (terminalNames.Count == 0)
        {
            return false;
        }

        return terminalNames.All(name =>
        {
            var normalized = TenantIdentityPorts.Normalize(name);
            return normalized == "id"
                || normalized == "tenantid"
                || normalized == "currenttenantid"
                || normalized.Contains("tenantid", StringComparison.Ordinal)
                || normalized.EndsWith("tenantid", StringComparison.Ordinal);
        });
    }
}
