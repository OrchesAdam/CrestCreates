using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>Symbol-identity-keyed taint flags for one analysed file.</summary>
internal sealed class TenantSymbolFlags : System.Collections.Generic.Dictionary<Microsoft.CodeAnalysis.ISymbol, bool>
{
    public TenantSymbolFlags()
        : base(Microsoft.CodeAnalysis.SymbolEqualityComparer.Default)
    {
    }

    public TenantSymbolFlags(TenantSymbolFlags other)
        : base(other, Microsoft.CodeAnalysis.SymbolEqualityComparer.Default)
    {
    }
}

internal readonly record struct TaintValue(bool IsNameTainted, string? Source, bool IsUndecidable, string? Reason)
{
    public static readonly TaintValue Clean = new(false, null, false, null);

    public static TaintValue Tainted(string source) => new(true, source, false, null);

    public static TaintValue Undecidable(string reason) => new(false, null, true, reason);

    public static TaintValue Merge(ReadOnlySpan<TaintValue> values)
    {
        var tainted = default(TaintValue?);
        var undecidable = default(TaintValue?);
        foreach (var value in values)
        {
            if (value.IsNameTainted && tainted is null)
            {
                tainted = value;
            }

            if (value.IsUndecidable && undecidable is null)
            {
                undecidable = value;
            }
        }

        return tainted ?? undecidable ?? Clean;
    }
}

/// <summary>
/// Bounded intra-method value flow with one-level same-project helper inlining. Tainted means
/// "this expression can evaluate to a tenant name", not "this expression is a string".
/// </summary>
internal sealed class TenantIdentityFlow(
    string projectName,
    string assemblyName,
    SemanticModel model,
    TenantSymbolFlags environment,
    Func<SyntaxTree, SemanticModel?>? modelForTree = null,
    TenantIdentityHelperIndex? helpers = null)
{
    private const int MaxInlineDepth = 2;
    private readonly HashSet<ISymbol> _inlining = new(SymbolEqualityComparer.Default);

    public TaintValue Evaluate(ExpressionSyntax? expression, int depth = 0)
    {
        if (expression is null)
        {
            return TaintValue.Clean;
        }

        var stripped = Unwrap(expression);
        switch (stripped)
        {
            case LiteralExpressionSyntax literal:
                return literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.IsKind(SyntaxKind.InterpolatedStringExpression)
                    ? EvaluateLiteral(literal)
                    : TaintValue.Clean;
            case InterpolatedStringExpressionSyntax interpolated:
                return TaintValue.Merge(interpolated.Contents
                    .OfType<InterpolationSyntax>()
                    .Select(hole => Evaluate(hole.Expression, depth))
                    .ToArray());
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.CoalesceExpression):
                return TaintValue.Merge([Evaluate(binary.Left, depth), Evaluate(binary.Right, depth)]);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.IsExpression) || binary.IsKind(SyntaxKind.AsExpression):
                return TaintValue.Merge([Evaluate(binary.Left, depth), Evaluate(binary.Right, depth)]);
            case BinaryExpressionSyntax:
                return TaintValue.Clean;
            case ParenthesizedExpressionSyntax parenthesized:
                return Evaluate(parenthesized.Expression, depth);
            case CastExpressionSyntax cast:
                return Evaluate(cast.Expression, depth);
            case AwaitExpressionSyntax awaitExpression:
                return Evaluate(awaitExpression.Expression, depth);
            case ConditionalExpressionSyntax conditional:
                return TaintValue.Merge([Evaluate(conditional.WhenTrue, depth), Evaluate(conditional.WhenFalse, depth)]);
            case ConditionalAccessExpressionSyntax conditionalAccess:
                return TaintValue.Merge([
                    Evaluate(conditionalAccess.Expression, depth),
                    Evaluate(conditionalAccess.WhenNotNull, depth),
                ]);
            case MemberBindingExpressionSyntax binding:
                return EvaluateSymbol(model.GetSymbolInfo(binding).Symbol, binding.ToString(), depth);
            case MemberAccessExpressionSyntax memberAccess:
                return EvaluateMemberAccess(memberAccess, depth);
            case InvocationExpressionSyntax invocation when InvokedName(invocation.Expression) == "nameof":
                // nameof yields the declared identifier as a constant, never a runtime tenant name value.
                return TaintValue.Clean;
            case InvocationExpressionSyntax invocation:
                return EvaluateInvocation(invocation, depth);
            case SwitchExpressionSyntax switchExpression:
                return TaintValue.Merge(switchExpression.Arms.Select(arm => Evaluate(arm.Expression, depth)).ToArray());
            case ObjectCreationExpressionSyntax creation:
                return EvaluateObjectCreation(creation, depth);
            case ElementAccessExpressionSyntax elementAccess:
                return TaintValue.Merge([
                    Evaluate(elementAccess.Expression, depth),
                    .. elementAccess.ArgumentList.Arguments.Select(argument => Evaluate(argument.Expression, depth)),
                ]);
            case AssignmentExpressionSyntax assignment:
                return EvaluateAssignment(assignment, depth);
            case ThrowExpressionSyntax throwExpression:
                return Evaluate(throwExpression.Expression, depth);
            case ImplicitElementAccessSyntax implicitAccess:
                return TaintValue.Merge(implicitAccess.ArgumentList.Arguments.Select(argument => Evaluate(argument.Expression, depth)).ToArray());
            case TupleExpressionSyntax tuple:
                return TaintValue.Merge(tuple.Arguments.Select(argument => Evaluate(argument.Expression, depth)).ToArray());
            case IsPatternExpressionSyntax pattern:
                return TaintValue.Merge([
                    Evaluate(pattern.Expression, depth),
                    EvaluateDesignation(pattern.Pattern, depth),
                ]);
            case ExpressionSyntax simple:
                return EvaluateSymbol(model.GetSymbolInfo(simple).Symbol, simple.ToString(), depth);
            default:
                return TaintValue.Clean;
        }
    }

    private TaintValue EvaluateDesignation(PatternSyntax? pattern, int depth)
    {
        _ = depth;
        if (pattern is DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax single }
            && model.GetDeclaredSymbol(single) is { } symbol)
        {
            return EvaluateSymbol(symbol, single.Identifier.ValueText, 0);
        }

        return TaintValue.Clean;
    }

    private TaintValue EvaluateLiteral(LiteralExpressionSyntax literal)
    {
        if (literal.Token.Value is not string text)
        {
            return TaintValue.Clean;
        }

        // A literal that itself looks like a tenant-name accessor is still a constant, never a name value.
        _ = text;
        return TaintValue.Clean;
    }

    private TaintValue EvaluateAssignment(AssignmentExpressionSyntax assignment, int depth)
    {
        var value = Evaluate(assignment.Right, depth);
        if (model.GetSymbolInfo(assignment.Left).Symbol is { } symbol)
        {
            if (value.IsNameTainted)
            {
                environment[symbol] = true;
            }
            else if (environment.ContainsKey(symbol))
            {
                environment[symbol] = false;
            }
        }

        return value;
    }

    private TaintValue EvaluateMemberAccess(MemberAccessExpressionSyntax memberAccess, int depth)
    {
        var symbol = model.GetSymbolInfo(memberAccess).Symbol;
        if (symbol is null)
        {
            var receiver = Evaluate(memberAccess.Expression, depth);
            if (receiver.IsNameTainted || receiver.IsUndecidable)
            {
                return receiver;
            }

            return IsSensitiveText(memberAccess.Name.Identifier.ValueText)
                ? TaintValue.Undecidable($"'{memberAccess}' did not bind")
                : TaintValue.Clean;
        }

        var own = EvaluateSymbol(symbol, memberAccess.ToString(), depth);
        if (own.IsNameTainted || own.IsUndecidable)
        {
            return own;
        }

        // A name-tainted *object* is not a name-tainted value: only a string-ish receiver can flow through
        // a member access (for example tenant.Name.Trim()), so ask the receiver only when it is a string.
        var receiverType = model.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType?.SpecialType is SpecialType.System_String)
        {
            return Evaluate(memberAccess.Expression, depth);
        }

        return TaintValue.Clean;
    }

    private TaintValue EvaluateSymbol(ISymbol? symbol, string display, int depth)
    {
        if (symbol is null)
        {
            return IsSensitiveText(display)
                ? TaintValue.Undecidable($"'{Trim(display)}' did not bind to a symbol")
                : TaintValue.Clean;
        }

        _ = depth;
        if (symbol is IPropertySymbol or IFieldSymbol or IParameterSymbol or ILocalSymbol or IEventSymbol)
        {
            if (IsNameSourceSymbol(symbol))
            {
                return TaintValue.Tainted(DescribeSource(symbol));
            }

            if (environment.TryGetValue(symbol, out var tainted) && tainted)
            {
                return TaintValue.Tainted($"alias {symbol.Name} <- {DescribeSource(symbol)}");
            }
        }

        if (symbol is INamedTypeSymbol)
        {
            return TaintValue.Clean;
        }

        return TaintValue.Clean;
    }

    private static bool IsNameSourceSymbol(ISymbol symbol)
    {
        if (TenantIdentityPorts.IsTenantNameMemberName(symbol.Name))
        {
            return true;
        }

        if (symbol is not IPropertySymbol property)
        {
            return false;
        }

        if (!TenantIdentityPorts.TenantNamePropertyNames.Contains(property.Name, StringComparer.Ordinal))
        {
            return false;
        }

        var containing = property.ContainingType;
        return FullName(containing) == TenantIdentityPorts.TenantEntityType
            || containing.AllInterfaces.Any(@interface => FullName(@interface) == TenantIdentityPorts.TenantInfoType)
            || FullName(containing) == TenantIdentityPorts.TenantInfoType;
    }

    private TaintValue EvaluateInvocation(InvocationExpressionSyntax invocation, int depth)
    {
        var symbol = model.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol
            ?? model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        var name = InvokedName(invocation.Expression);
        if (symbol is null || symbol.ReturnType.TypeKind == TypeKind.Error || symbol.ContainingType?.TypeKind == TypeKind.Error)
        {
            var arguments = invocation.ArgumentList.Arguments
                .Select(argument => Evaluate(argument.Expression, depth))
                .ToArray();
            var propagated = TaintValue.Merge(arguments);
            if (propagated.IsNameTainted)
            {
                return propagated;
            }

            return IsSensitiveText(name) || arguments.Any(value => value.IsUndecidable)
                ? TaintValue.Undecidable($"'{Trim(invocation.ToString())}' did not bind to a method symbol")
                : TaintValue.Clean;
        }

        var parts = new List<TaintValue>(invocation.ArgumentList.Arguments.Count + 1)
        {
            invocation.Expression is MemberAccessExpressionSyntax access
                ? Evaluate(access.Expression, depth)
                : TaintValue.Clean,
        };

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            parts.Add(Evaluate(argument.Expression, depth));
        }

        var reduced = symbol.ReducedFrom ?? symbol;
        if (IsStringPropagator(reduced))
        {
            return TaintValue.Merge(parts.ToArray());
        }

        if (depth < MaxInlineDepth)
        {
            TaintValue inlined = default;
            var attempted = false;
            var summarized = false;

            if (IsSameProjectMethod(reduced) && _inlining.Add(reduced.OriginalDefinition))
            {
                attempted = true;
                inlined = InlineBody(reduced, model, assemblyName, CallArguments(invocation), depth + 1);
                _inlining.Remove(reduced.OriginalDefinition);
            }
            else if (helpers is not null && _inlining.Add(reduced.OriginalDefinition))
            {
                attempted = true;
                var foreign = helpers.Resolve(projectName, FullName(reduced.ContainingType), reduced.Name, reduced.Parameters.Length);
                if (foreign is not null)
                {
                    inlined = InlineBody(foreign.Symbol.OriginalDefinition, foreign.Model, foreign.AssemblyName, CallArguments(invocation), depth + 1);
                    summarized = true;
                }

                _inlining.Remove(reduced.OriginalDefinition);
            }

            if (summarized)
            {
                // A first-party helper resolved through the declaration index is summarised from its real
                // source: the answer is definitive in both directions.
                return inlined;
            }

            if (attempted && (inlined.IsNameTainted || inlined.IsUndecidable))
            {
                return inlined;
            }

            if (attempted && IsSameProjectMethod(reduced))
            {
                // Fail closed: a same-project helper whose body the guard could not summarise still
                // propagates a tainted receiver or argument instead of reporting clean.
                return TaintValue.Merge(parts.ToArray());
            }
        }

        var mergedParts = TaintValue.Merge(parts.ToArray());
        if (mergedParts.IsNameTainted)
        {
            return TaintValue.Tainted($"{reduced.Name}(...) of {DescribeSource(reduced)}");
        }

        if (mergedParts.IsUndecidable)
        {
            return mergedParts;
        }

        if (IsStringLike(reduced.ReturnType)
            && (IsSensitiveText(reduced.Name) || SymbolChainMentionsName(reduced)))
        {
            return TaintValue.Undecidable(
                $"'{reduced.ContainingType.Name}.{reduced.Name}' returns {reduced.ReturnType.Name} and may carry a tenant name; no same-project summary");
        }

        return TaintValue.Clean;
    }

    private TaintValue EvaluateObjectCreation(ObjectCreationExpressionSyntax creation, int depth)
    {
        var constructor = model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
        if (constructor is null)
        {
            var arguments = creation.ArgumentList?.Arguments
                .Select(argument => Evaluate(argument.Expression, depth))
                .ToArray() ?? [];
            return TaintValue.Merge(arguments);
        }

        if (depth < MaxInlineDepth && IsSameProjectMethod(constructor) && _inlining.Add(constructor.OriginalDefinition))
        {
            var inlined = InlineBody(constructor, model, assemblyName, CallArguments(creation), depth + 1);
            _inlining.Remove(constructor.OriginalDefinition);
            return inlined;
        }

        return TaintValue.Clean;
    }

    /// <summary>
    /// Substitute the call arguments for the helper parameters and evaluate every value the body can
    /// return. Works for a helper in another file of the same project and for a first-party helper that
    /// lives in another project (resolved through the declaration index).
    /// </summary>
    private TaintValue InlineBody(
        IMethodSymbol method,
        SemanticModel bodyModel,
        string bodyAssembly,
        IReadOnlyList<ExpressionSyntax> arguments,
        int depth)
    {
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is null)
        {
            return TaintValue.Clean;
        }

        // The helper body must be read through the model of the tree that declares it, not the call site's.
        var effectiveModel = ReferenceEquals(syntax.SyntaxTree, bodyModel.SyntaxTree)
            ? bodyModel
            : modelForTree?.Invoke(syntax.SyntaxTree)
              ?? bodyModel;

        if (!ReferenceEquals(syntax.SyntaxTree, effectiveModel.SyntaxTree))
        {
            return TaintValue.Undecidable($"'{method.Name}' body tree has no model in this compilation");
        }

        var localEnvironment = new TenantSymbolFlags();
        var results = new List<TaintValue>();

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Ordinal >= arguments.Count)
            {
                continue;
            }

            var value = Evaluate(arguments[parameter.Ordinal], depth);
            localEnvironment[parameter] = value.IsNameTainted;
            if (value.IsUndecidable)
            {
                results.Add(value);
            }
        }

        var nested = new TenantIdentityFlow(projectName, bodyAssembly, effectiveModel, localEnvironment, modelForTree, helpers);

        if (syntax is MethodDeclarationSyntax methodDeclaration)
        {
            if (methodDeclaration.ExpressionBody is not null)
            {
                results.Add(nested.Evaluate(methodDeclaration.ExpressionBody.Expression, depth));
            }

            foreach (var returned in methodDeclaration.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                results.Add(nested.Evaluate(returned.Expression, depth));
            }
        }
        else if (syntax is ConstructorDeclarationSyntax constructorDeclaration)
        {
            if (constructorDeclaration.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer) == true)
            {
                results.Add(nested.EvaluateArguments(constructorDeclaration.Initializer.ArgumentList, depth));
            }

            foreach (var returned in constructorDeclaration.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                results.Add(nested.Evaluate(returned.Expression, depth));
            }
        }

        // A helper that routes a name through a field or a local before returning it still needs those
        // writes evaluated against the substituted parameters.
        foreach (var declarator in syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer is { } initializer)
            {
                results.Add(nested.Evaluate(initializer.Value, depth));
            }
        }

        return TaintValue.Merge(results.ToArray());
    }

    private static IReadOnlyList<ExpressionSyntax> CallArguments(ExpressionSyntax call) => call switch
    {
        InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments.Select(argument => argument.Expression).ToArray(),
        ObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments.Select(argument => argument.Expression).ToArray() ?? [],
        ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.ArgumentList?.Arguments.Select(argument => argument.Expression).ToArray() ?? [],
        _ => [],
    };

    public TaintValue EvaluateArguments(BaseArgumentListSyntax? argumentList, int depth)
    {
        if (argumentList is null)
        {
            return TaintValue.Clean;
        }

        return TaintValue.Merge(argumentList.Arguments.Select(argument => Evaluate(argument.Expression, depth)).ToArray());
    }

    private bool IsSameProjectMethod(IMethodSymbol method)
    {
        if (method.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        var containingAssembly = method.ContainingAssembly?.Name;
        return containingAssembly is null || string.Equals(containingAssembly, assemblyName, StringComparison.Ordinal);
    }

    private static bool IsStringPropagator(IMethodSymbol method)
    {
        var containing = method.ContainingType?.SpecialType;
        if (containing is SpecialType.System_String or SpecialType.System_Object or SpecialType.System_ValueType)
        {
            return true;
        }

        if (method.ContainingType?.Name is "Convert" or "StringExtensions" or "TenantIdentifierNormalizer")
        {
            return true;
        }

        return method is { Name: "ToString", Parameters.Length: 0 };
    }

    private static bool IsStringLike(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_String or SpecialType.System_Object
            || type.SpecialType >= SpecialType.System_Byte && type.SpecialType <= SpecialType.System_Decimal;

    private static bool SymbolChainMentionsName(IMethodSymbol method)
    {
        var name = TenantIdentityPorts.Normalize($"{method.ContainingType?.Name}.{method.Name}");
        return name.Contains("name", StringComparison.Ordinal);
    }

    private static bool IsSensitiveText(string text)
    {
        var normalized = TenantIdentityPorts.Normalize(text);
        return normalized.Contains("tenant", StringComparison.Ordinal) || normalized.Contains("name", StringComparison.Ordinal);
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
        CastExpressionSyntax cast => Unwrap(cast.Expression),
        AwaitExpressionSyntax awaited => Unwrap(awaited.Expression),
        PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) => Unwrap(postfix.Operand),
        _ => expression,
    };

    private static string InvokedName(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => string.Empty,
    };

    private static string DescribeSource(ISymbol symbol)
    {
        var containing = symbol.ContainingType is { } type ? $"{type.Name}." : string.Empty;
        return $"{containing}{symbol.Name}";
    }

    private static string FullName(ISymbol symbol) =>
        TenantIdentitySymbols.FullName(symbol);

    private static string Trim(string value)
    {
        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= 120 ? collapsed : collapsed[..120];
    }
}
