using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>Symbol-identity-keyed flow state for one analysed file or helper body.</summary>
internal sealed class TenantTaintEnvironment : Dictionary<ISymbol, TaintValue>
{
    public TenantTaintEnvironment()
        : base(SymbolEqualityComparer.Default)
    {
    }

    public TenantTaintEnvironment(TenantTaintEnvironment other)
        : base(other, SymbolEqualityComparer.Default)
    {
    }
}

/// <summary>
/// A value's tenant-name status together with the members it provably originates from. The origin set is what
/// the authoritative-source ledger checks: a value named <c>tenantId</c> that actually came from a user name
/// must not be treated as identity.
/// </summary>
internal readonly record struct TaintValue(
    bool IsNameTainted,
    string? Source,
    bool IsUndecidable,
    string? Reason,
    IReadOnlyList<string> Origins)
{
    public static readonly TaintValue Clean = new(false, null, false, null, []);

    public static TaintValue Tainted(string source, IReadOnlyList<string> origins) =>
        new(true, source, false, null, origins);

    public static TaintValue Undecidable(string reason, IReadOnlyList<string> origins) =>
        new(false, null, true, reason, origins.Count == 0 ? ["unknown"] : origins);

    public static TaintValue FromOrigins(IReadOnlyList<string> origins) =>
        origins.Count == 0 ? Clean : new(false, null, false, null, origins);

    public static TaintValue Merge(ReadOnlySpan<TaintValue> values)
    {
        var tainted = default(TaintValue?);
        var undecidable = default(TaintValue?);
        var origins = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

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

            foreach (var origin in value.Origins)
            {
                if (seen.Add(origin))
                {
                    origins.Add(origin);
                }
            }
        }

        var result = tainted ?? undecidable ?? Clean;
        return result with { Origins = origins };
    }
}

/// <summary>
/// Bounded intra-method value flow with one-level same-project helper inlining. Tainted means "this
/// expression can evaluate to a tenant name", not "this expression is a string"; undecidable means the guard
/// could not prove either way and must surface as a coverage failure, including when the value travels
/// through a local variable or an assignment.
/// </summary>
internal sealed class TenantIdentityFlow(
    string projectName,
    string assemblyName,
    SemanticModel model,
    TenantTaintEnvironment environment,
    Func<SyntaxTree, SemanticModel?>? modelForTree = null,
    TenantIdentityHelperIndex? helpers = null,
    IReadOnlySet<ISymbol>? substitutedParameters = null)
{
    private const int MaxInlineDepth = 2;
    private readonly HashSet<ISymbol> _inlining = new(SymbolEqualityComparer.Default);
    private readonly IReadOnlySet<ISymbol> _substitutedParameters = substitutedParameters ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default);

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
                return EvaluateLiteral(literal);
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
            case IsPatternExpressionSyntax pattern:
                return Evaluate(pattern.Expression, depth);
            case ImplicitElementAccessSyntax implicitAccess:
                return TaintValue.Merge(implicitAccess.ArgumentList.Arguments.Select(argument => Evaluate(argument.Expression, depth)).ToArray());
            case TupleExpressionSyntax tuple:
                return TaintValue.Merge(tuple.Arguments.Select(argument => Evaluate(argument.Expression, depth)).ToArray());
            case ExpressionSyntax simple:
                return EvaluateSymbol(model.GetSymbolInfo(simple).Symbol, simple.ToString(), depth);
            default:
                return TaintValue.Clean;
        }
    }

    /// <summary>
    /// A literal is normally a neutral value, but a non-empty constant fallback still produces a runtime value:
    /// erasing it would make <c>context.TenantId ?? "acme-east"</c> indistinguishable from an empty fallback and
    /// let a name masquerade as identity. Null, default, empty and whitespace-free neutral constants stay clean.
    /// </summary>
    private static TaintValue EvaluateLiteral(LiteralExpressionSyntax literal)
    {
        if (literal.IsKind(SyntaxKind.NullLiteralExpression)
            || literal.IsKind(SyntaxKind.DefaultLiteralExpression)
            || literal.Token.Value is not string text
            || text.Length == 0)
        {
            return TaintValue.Clean;
        }

        return TaintValue.FromOrigins([$"literal:\"{text}\""]);
    }

    private TaintValue EvaluateAssignment(AssignmentExpressionSyntax assignment, int depth)
    {
        var value = Evaluate(assignment.Right, depth);
        if (model.GetSymbolInfo(assignment.Left).Symbol is { } symbol)
        {
            environment[symbol] = value;
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
                ? TaintValue.Undecidable($"'{memberAccess}' did not bind", [])
                : TaintValue.Clean;
        }

        var own = EvaluateSymbol(symbol, memberAccess.ToString(), depth);
        if (own.IsNameTainted || own.IsUndecidable || own.Origins.Count > 0)
        {
            return own;
        }

        // A name-tainted *object* is not a name-tainted value: only a string-ish receiver can flow through a
        // member access (for example tenant.Name.Trim()), so ask the receiver only when it is a string.
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
                ? TaintValue.Undecidable($"'{Trim(display)}' did not bind to a symbol", [])
                : TaintValue.Clean;
        }

        _ = depth;

        var baseValue = _substitutedParameters.Contains(symbol) ? TaintValue.Clean : BaseValueOf(symbol);
        // A local or field keeps the whole state of what was last written to it: dropping the undecidable
        // flag here is exactly how an unknown value silently becomes "clean" after one assignment. The
        // symbol's own provenance is merged in so a parameter keeps its role even when it was seeded clean.
        if (environment.TryGetValue(symbol, out var stored))
        {
            return TaintValue.Merge([baseValue, stored]);
        }

        return baseValue;
    }

    private static TaintValue BaseValueOf(ISymbol symbol)
    {
        if (IsNameSourceSymbol(symbol))
        {
            return TaintValue.Tainted(DescribeSource(symbol), [OriginOf(symbol)]);
        }

        // Static members such as string.Empty are constants, not competing tenant sources.
        if (symbol is IPropertySymbol { IsStatic: true } or IFieldSymbol { IsStatic: true })
        {
            return TaintValue.Clean;
        }

        return symbol is IPropertySymbol or IFieldSymbol or IParameterSymbol
            ? TaintValue.FromOrigins([OriginOf(symbol)])
            : TaintValue.Clean;
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
        return TenantIdentitySymbols.FullName(containing) == TenantIdentityPorts.TenantEntityType
            || containing.AllInterfaces.Any(@interface => TenantIdentitySymbols.FullName(@interface) == TenantIdentityPorts.TenantInfoType)
            || TenantIdentitySymbols.FullName(containing) == TenantIdentityPorts.TenantInfoType;
    }

    private TaintValue EvaluateInvocation(InvocationExpressionSyntax invocation, int depth)
    {
        var symbol = model.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol
            ?? model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        var name = InvokedName(invocation.Expression);
        var arguments = model.GetOperation(invocation) is IInvocationOperation operation
            ? ArgumentMap(operation.Arguments)
            : PositionalMap(invocation);

        if (symbol is null || symbol.ReturnType.TypeKind == TypeKind.Error || symbol.ContainingType?.TypeKind == TypeKind.Error)
        {
            var evaluated = invocation.ArgumentList.Arguments.Select(argument => Evaluate(argument.Expression, depth)).ToArray();
            var propagated = TaintValue.Merge(evaluated);
            if (propagated.IsNameTainted)
            {
                return propagated;
            }

            return IsSensitiveText(name) || evaluated.Any(value => value.IsUndecidable)
                ? TaintValue.Undecidable($"'{Trim(invocation.ToString())}' did not bind to a method symbol", propagated.Origins)
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
        if (IsValuePreserving(reduced))
        {
            return TaintValue.Merge(parts.ToArray());
        }

        if (depth < MaxInlineDepth && _inlining.Add(reduced.OriginalDefinition))
        {
            if (IsSameProjectMethod(reduced))
            {
                var inlined = InlineBody(reduced, model, assemblyName, arguments, depth + 1);
                _inlining.Remove(reduced.OriginalDefinition);
                if (inlined.IsNameTainted || inlined.IsUndecidable || inlined.Origins.Count > 0)
                {
                    return inlined;
                }

                // Fail closed: a same-project helper whose body the guard could not summarise still propagates
                // a tainted receiver or argument instead of reporting clean.
                return TaintValue.Merge(parts.ToArray());
            }

            if (helpers is not null)
            {
                var foreign = helpers.Resolve(
                    projectName,
                    TenantIdentitySymbols.FullName(reduced.ContainingType),
                    reduced.Name,
                    reduced.Parameters.Length);
                if (foreign is not null)
                {
                    var inlined = InlineBody(foreign.Symbol.OriginalDefinition, foreign.Model, foreign.AssemblyName, arguments, depth + 1);
                    _inlining.Remove(reduced.OriginalDefinition);
                    return inlined;
                }
            }

            _inlining.Remove(reduced.OriginalDefinition);
        }

        var mergedParts = TaintValue.Merge(parts.ToArray());
        if (mergedParts.IsNameTainted)
        {
            return TaintValue.Tainted($"{reduced.Name}(...) of {DescribeSource(reduced)}", mergedParts.Origins);
        }

        if (mergedParts.IsUndecidable)
        {
            return mergedParts;
        }

        if (IsStringLike(reduced.ReturnType)
            && (IsSensitiveText(reduced.Name)
                || TenantIdentityPorts.Normalize($"{reduced.ContainingType?.Name}{reduced.Name}").Contains("name", StringComparison.Ordinal)))
        {
            return TaintValue.Undecidable(
                $"'{reduced.ContainingType?.Name}.{reduced.Name}' returns {reduced.ReturnType.Name} and may carry a tenant name; no summary",
                mergedParts.Origins);
        }

        // An external call is not an authoritative source, but the arguments it received stay visible.
        return TaintValue.FromOrigins(mergedParts.Origins);
    }

    private TaintValue EvaluateObjectCreation(ObjectCreationExpressionSyntax creation, int depth)
    {
        var constructor = model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
        var arguments = model.GetOperation(creation) is IObjectCreationOperation operation
            ? ArgumentMap(operation.Arguments)
            : PositionalMap(creation);

        if (constructor is null)
        {
            return TaintValue.Merge(creation.ArgumentList?.Arguments
                .Select(argument => Evaluate(argument.Expression, depth))
                .ToArray() ?? []);
        }

        if (depth < MaxInlineDepth && IsSameProjectMethod(constructor) && _inlining.Add(constructor.OriginalDefinition))
        {
            var inlined = InlineBody(constructor, model, assemblyName, arguments, depth + 1);
            _inlining.Remove(constructor.OriginalDefinition);
            return inlined;
        }

        return TaintValue.Merge(creation.ArgumentList?.Arguments
            .Select(argument => Evaluate(argument.Expression, depth))
            .ToArray() ?? []);
    }

    /// <summary>
    /// Inlines a helper: bind the call arguments to the declared parameters by symbol, run the helper body's own
    /// declarations and assignments to a fixed point, and only then evaluate its return values. Merging every
    /// initialiser in the body would both miss plain assignments and misreport helpers that correctly resolve a
    /// directory entity.
    /// </summary>
    private TaintValue InlineBody(
        IMethodSymbol method,
        SemanticModel bodyModel,
        string bodyAssembly,
        IReadOnlyDictionary<int, ExpressionSyntax> arguments,
        int depth)
    {
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is null)
        {
            return TaintValue.Clean;
        }

        var effectiveModel = ReferenceEquals(syntax.SyntaxTree, bodyModel.SyntaxTree)
            ? bodyModel
            : modelForTree?.Invoke(syntax.SyntaxTree) ?? bodyModel;

        if (!ReferenceEquals(syntax.SyntaxTree, effectiveModel.SyntaxTree))
        {
            return TaintValue.Undecidable($"'{method.Name}' body tree has no model in this compilation", []);
        }

        var localEnvironment = new TenantTaintEnvironment();
        var substitutedParameters = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var parameter in method.Parameters)
        {
            if (arguments.TryGetValue(parameter.Ordinal, out var argument))
            {
                localEnvironment[parameter] = Evaluate(argument, depth);
                substitutedParameters.Add(parameter);
            }
        }

        var writes = CollectWrites(syntax, effectiveModel);
        for (var pass = 0; pass < 6; pass++)
        {
            var changed = false;
            var nested = new TenantIdentityFlow(projectName, bodyAssembly, effectiveModel, localEnvironment, modelForTree, helpers, substitutedParameters);
            foreach (var write in writes)
            {
                var before = localEnvironment.TryGetValue(write.Symbol, out var existing) ? existing : TaintValue.Clean;
                var merged = TaintValue.Merge([before, nested.Evaluate(write.Expression, depth)]);
                if (IsMoreInformative(merged, before))
                {
                    localEnvironment[write.Symbol] = merged;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var flow = new TenantIdentityFlow(projectName, bodyAssembly, effectiveModel, localEnvironment, modelForTree, helpers, substitutedParameters);
        var results = new List<TaintValue>();
        switch (syntax)
        {
            case MethodDeclarationSyntax methodDeclaration:
                if (methodDeclaration.ExpressionBody is not null)
                {
                    results.Add(flow.Evaluate(methodDeclaration.ExpressionBody.Expression, depth));
                }

                results.AddRange(methodDeclaration.DescendantNodes()
                    .OfType<ReturnStatementSyntax>()
                    .Where(statement => statement.Expression is not null)
                    .Select(statement => flow.Evaluate(statement.Expression, depth)));
                break;
            case ConstructorDeclarationSyntax constructorDeclaration:
                if (constructorDeclaration.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer) == true)
                {
                    results.Add(flow.EvaluateArguments(constructorDeclaration.Initializer.ArgumentList, depth));
                }

                results.AddRange(constructorDeclaration.DescendantNodes()
                    .OfType<ReturnStatementSyntax>()
                    .Where(statement => statement.Expression is not null)
                    .Select(statement => flow.Evaluate(statement.Expression, depth)));
                break;
        }

        return TaintValue.Merge(results.ToArray());
    }

    private static bool IsMoreInformative(TaintValue candidate, TaintValue previous) =>
        candidate.IsNameTainted != previous.IsNameTainted
        || candidate.IsUndecidable != previous.IsUndecidable
        || candidate.Origins.Count != previous.Origins.Count;

    private static IReadOnlyList<(ISymbol Symbol, ExpressionSyntax Expression)> CollectWrites(SyntaxNode body, SemanticModel bodyModel)
    {
        var writes = new List<(ISymbol, ExpressionSyntax)>();
        foreach (var declarator in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer is not null && bodyModel.GetDeclaredSymbol(declarator) is { } symbol)
            {
                writes.Add((symbol, declarator.Initializer.Value));
            }
        }

        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                     .Where(node => node.IsKind(SyntaxKind.SimpleAssignmentExpression)
                                    || node.IsKind(SyntaxKind.CoalesceAssignmentExpression)))
        {
            if (bodyModel.GetSymbolInfo(assignment.Left).Symbol is { } symbol)
            {
                writes.Add((symbol, assignment.Right));
            }
        }

        return writes;
    }

    private static IReadOnlyDictionary<int, ExpressionSyntax> ArgumentMap(IEnumerable<IArgumentOperation> arguments)
    {
        var map = new Dictionary<int, ExpressionSyntax>();
        foreach (var argument in arguments)
        {
            if (argument.Parameter is not null && argument.Value.Syntax is ExpressionSyntax expression)
            {
                map[argument.Parameter.Ordinal] = expression;
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<int, ExpressionSyntax> PositionalMap(InvocationExpressionSyntax invocation)
    {
        var map = new Dictionary<int, ExpressionSyntax>();
        for (var index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
        {
            map[index] = invocation.ArgumentList.Arguments[index].Expression;
        }

        return map;
    }

    private static IReadOnlyDictionary<int, ExpressionSyntax> PositionalMap(ObjectCreationExpressionSyntax creation)
    {
        var map = new Dictionary<int, ExpressionSyntax>();
        var arguments = creation.ArgumentList?.Arguments ?? default;
        for (var index = 0; index < (creation.ArgumentList?.Arguments.Count ?? 0); index++)
        {
            map[index] = arguments[index].Expression;
        }

        return map;
    }

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

    private static bool IsValuePreserving(IMethodSymbol method) =>
        method.Name is "ToString" or "Trim" or "TrimStart" or "TrimEnd" or "ToLower" or "ToLowerInvariant"
            or "ToUpper" or "ToUpperInvariant" or "AsSpan";

    private static bool IsStringLike(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_String or SpecialType.System_Object
            || type.SpecialType >= SpecialType.System_Byte && type.SpecialType <= SpecialType.System_Decimal;

    private static bool IsSensitiveText(string text)
    {
        var normalized = TenantIdentityPorts.Normalize(text);
        return normalized.Contains("tenant", StringComparison.Ordinal) || normalized.Contains("name", StringComparison.Ordinal);
    }

    internal static string OriginOf(ISymbol symbol) => symbol switch
    {
        IParameterSymbol parameter =>
            $"param:{TenantIdentitySymbols.FullName(parameter.ContainingType ?? parameter.ContainingSymbol as INamedTypeSymbol)}:{parameter.Name}",
        IPropertySymbol property => $"{TenantIdentitySymbols.FullName(property.ContainingType)}.{property.Name}",
        IFieldSymbol field => $"{TenantIdentitySymbols.FullName(field.ContainingType)}.{field.Name}",
        IMethodSymbol method => $"method:{TenantIdentitySymbols.FullName(method.ContainingType)}.{method.Name}",
        ILocalSymbol local => $"local:{local.Name}",
        _ => $"symbol:{symbol.Name}",
    };

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
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => string.Empty,
    };

    private static string DescribeSource(ISymbol symbol)
    {
        var containing = symbol.ContainingType is { } type ? $"{type.Name}." : string.Empty;
        return $"{containing}{symbol.Name}";
    }

    private static string Trim(string value)
    {
        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= 120 ? collapsed : collapsed[..120];
    }
}
