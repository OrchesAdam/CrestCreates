using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.DependencyBoundaries.Tests;

internal sealed record ExitObservation(
    string Project,
    string RelativePath,
    string PortLabel,
    TenantIdentityRule Rule,
    int Line,
    int Column,
    string ArgumentText,
    IReadOnlyList<string> TerminalNames,
    IReadOnlyList<string> Origins);

internal sealed class TenantIdentityAnalysis
{
    public List<TenantIdentityDiagnostic> Diagnostics { get; } = [];
    public List<ExitObservation> Exits { get; } = [];
    public List<string> CompiledProjects { get; } = [];
    public int ProjectsEnumerated { get; set; }
    public int FilesEnumerated { get; set; }
    public int FilesWithSensitiveCandidates { get; set; }
    public int NameSourceReadsAtExits { get; set; }
    public List<string> CompilationErrors { get; } = [];
    public int IndexedHelperMembers { get; set; }

    public IEnumerable<TenantIdentityDiagnostic> Violations =>
        Diagnostics.Where(diagnostic => diagnostic.Rule != TenantIdentityRule.TI900);

    public IEnumerable<TenantIdentityDiagnostic> CoverageFailures =>
        Diagnostics.Where(diagnostic => diagnostic.Rule == TenantIdentityRule.TI900);
}

/// <summary>
/// Repository-wide TenantId architecture guard. Each active production project is analysed inside its own
/// reference closure; a bounded intra-method value flow plus one-level same-project helper summaries decide
/// whether a tenant name can reach a tenant identity slot. Sensitive nodes that do not bind are reported as
/// coverage failures instead of being skipped. It is a bounded pattern guard, not a general data-flow or
/// security analyser (#131 design §4.2).
/// </summary>
internal sealed class TenantIdentityAnalyzer
{
    private readonly TenantIdentityAnalysis _analysis = new();

    public TenantIdentityAnalysis Analysis => _analysis;

    private readonly Dictionary<string, TenantProjectContext> _contexts = new(StringComparer.Ordinal);
    private TenantIdentityHelperIndex? _helpers;

    public void AnalyzeRepository(string repoRoot)
    {
        var inputs = TenantIdentityProjectInventory
            .EnumerateProjectFiles(repoRoot)
            .Select(projectFile => TenantIdentityProjectInventory.Load(repoRoot, projectFile))
            .ToArray();

        _helpers = new TenantIdentityHelperIndex(key => Context(inputs.Single(input => input.Project == key)));

        foreach (var input in inputs)
        {
            _helpers.RegisterProject(input);
            _analysis.ProjectsEnumerated++;
            _analysis.FilesEnumerated += input.Sources.Count;

            if (input.EvaluationError is not null)
            {
                // "I could not obtain the inputs" is not "there is nothing to analyse": report before any
                // source-based filtering, otherwise an empty or unknown Compile set silently passes.
                _analysis.Diagnostics.Add(new TenantIdentityDiagnostic(
                    TenantIdentityRule.TI900, input.Project, input.Project, 0, 0, "-", "-",
                    $"coverage failure: {input.EvaluationError}"));
            }
            else if (input.ReferenceError is not null && input.Sources.Any(HasSensitiveCandidate))
            {
                // The sources are known and contain tenant code, but the symbol context is missing.
                _analysis.Diagnostics.Add(new TenantIdentityDiagnostic(
                    TenantIdentityRule.TI900, input.Project, input.Project, 0, 0, "-", "-",
                    $"coverage failure: {input.ReferenceError}"));
            }
        }

        _analysis.IndexedHelperMembers = _helpers.RegisteredMembers;

        foreach (var input in inputs)
        {
            if (!input.Sources.Any(HasSensitiveCandidate) || input.ReferenceError is not null)
            {
                continue;
            }

            _analysis.CompiledProjects.Add(input.Project);
            var context = Context(input);
            foreach (var source in input.Sources.Where(HasSensitiveCandidate))
            {
                _analysis.FilesWithSensitiveCandidates++;
                AnalyzeFile(input, source.RelativePath, context);
            }
        }
    }

    public void AnalyzeProject(TenantProjectInput project)
    {
        _analysis.ProjectsEnumerated++;
        _analysis.FilesEnumerated += project.Sources.Count;

        if (project.EvaluationError is not null)
        {
            _analysis.Diagnostics.Add(new TenantIdentityDiagnostic(
                TenantIdentityRule.TI900, project.Project, project.Project, 0, 0, "-", "-",
                $"coverage failure: {project.EvaluationError}"));
            return;
        }

        if (project.ReferenceError is not null)
        {
            if (project.Sources.Any(HasSensitiveCandidate))
            {
                _analysis.Diagnostics.Add(new TenantIdentityDiagnostic(
                    TenantIdentityRule.TI900, project.Project, project.Project, 0, 0, "-", "-",
                    $"coverage failure: {project.ReferenceError}"));
            }

            return;
        }

        var candidates = project.Sources.Where(HasSensitiveCandidate).ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        _analysis.CompiledProjects.Add(project.Project);
        var context = Context(project);
        foreach (var source in candidates)
        {
            _analysis.FilesWithSensitiveCandidates++;
            AnalyzeFile(project, source.RelativePath, context);
        }
    }

    private TenantProjectContext Context(TenantProjectInput input)
    {
        if (!_contexts.TryGetValue(input.Project, out var context))
        {
            context = TenantProjectContext.Build(input);
            _contexts[input.Project] = context;

            foreach (var error in context.Compilation.GetDiagnostics()
                         .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                         .Take(12))
            {
                _analysis.CompilationErrors.Add($"{input.Project}: {error.Id} {error.GetMessage()} [{error.Location.GetLineSpan().Path}]");
            }
        }

        return context;
    }

    public static bool HasSensitiveCandidate(TenantSourceFile source)
    {
        var normalized = TenantIdentityPorts.Normalize(source.Text);
        return normalized.Contains("tenant", StringComparison.Ordinal)
            || TenantIdentityPorts.ExitMemberTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    private void AnalyzeFile(
        TenantProjectInput project,
        string relativePath,
        TenantProjectContext context)
    {
        var tree = context.TreesByPath[relativePath];
        var model = context.Models[tree];
        var root = tree.GetRoot();

        var writes = CollectWrites(model, root, project, relativePath).ToArray();
        var environment = new TenantTaintEnvironment();
        foreach (var write in writes)
        {
            environment.TryAdd(write.Symbol, TaintValue.Clean);
        }

        // Fixed point over declarations and assignments so a value keeps its tainted/undecidable state and its
        // origins after travelling through a local, a re-assignment or a branch merge.
        for (var pass = 0; pass < 6; pass++)
        {
            var changed = false;
            foreach (var write in writes.Where(entry => entry.Expression is not null))
            {
                var flow = new TenantIdentityFlow(project.Project, project.AssemblyName, model, environment, context.ModelFor, _helpers);
                var value = flow.Evaluate(write.Expression);
                var previous = environment.GetValueOrDefault(write.Symbol, TaintValue.Clean);
                var merged = TaintValue.Merge([previous, value]);
                if (merged.IsNameTainted != previous.IsNameTainted
                    || merged.IsUndecidable != previous.IsUndecidable
                    || merged.Origins.Count != previous.Origins.Count)
                {
                    environment[write.Symbol] = merged;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        foreach (var site in CollectExitSites(model, root, project, relativePath))
        {
            var flow = new TenantIdentityFlow(project.Project, project.AssemblyName, model, environment, context.ModelFor, _helpers);
            var value = flow.Evaluate(site.Expression);
            var position = site.Expression.GetLocation().GetLineSpan().StartLinePosition;

            _analysis.Exits.Add(new ExitObservation(
                project.Project,
                relativePath,
                site.Label,
                site.Rule,
                position.Line + 1,
                position.Character + 1,
                Collapse(site.Expression.ToString()),
                TenantIdentitySymbols.TerminalValueNames(model, site.Expression),
                value.Origins));

            if (value.IsNameTainted)
            {
                _analysis.NameSourceReadsAtExits++;
                _analysis.Diagnostics.Add(new TenantIdentityDiagnostic(
                    site.Rule,
                    project.Project,
                    relativePath,
                    position.Line + 1,
                    position.Character + 1,
                    value.Source ?? "tenant name",
                    site.Label,
                    "resolve the directory entity first and pass its Id; a tenant name is display and lookup text only"));
            }
            else if (value.IsUndecidable)
            {
                _analysis.Diagnostics.Add(new TenantIdentityDiagnostic(
                    TenantIdentityRule.TI900,
                    project.Project,
                    relativePath,
                    position.Line + 1,
                    position.Character + 1,
                    value.Reason ?? "undecidable",
                    site.Label,
                    "the guard cannot decide whether this value carries a tenant name; add a same-project helper summary or explicit evidence"));
            }
        }
    }

    private static string Collapse(string value)
    {
        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= 120 ? collapsed : collapsed[..120];
    }

    private IEnumerable<Write> CollectWrites(SemanticModel model, SyntaxNode root, TenantProjectInput project, string relativePath)
    {
        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (model.GetDeclaredSymbol(declarator) is not { } symbol)
            {
                continue;
            }

            yield return new Write(
                symbol,
                declarator.Initializer?.Value,
                IsIdentityName(symbol.Name),
                symbol.Name);
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                     .Where(node => node.IsKind(SyntaxKind.SimpleAssignmentExpression)
                                    || node.IsKind(SyntaxKind.CoalesceAssignmentExpression)))
        {
            if (model.GetSymbolInfo(assignment.Left).Symbol is not { } symbol)
            {
                if (assignment.Left is not (ElementAccessExpressionSyntax or ImplicitElementAccessSyntax)
                    && IsIdentityName(TerminalName(assignment.Left)))
                {
                    _analysis.Diagnostics.Add(BindFailure(project, relativePath, assignment.Left, "identity assignment target did not bind"));
                }

                continue;
            }

            yield return new Write(
                symbol,
                assignment.Right,
                IsIdentityName(symbol.Name),
                symbol.Name);
        }

        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (model.GetDeclaredSymbol(parameter) is not IParameterSymbol symbol)
            {
                continue;
            }

            if (IsIdentityName(symbol.Name) || TenantIdentityPorts.IsTenantNameMemberName(symbol.Name))
            {
                yield return new Write(symbol, null, IsIdentityName(symbol.Name), symbol.Name);
            }
        }
    }

    private static bool IsIdentityName(string name) => TenantIdentityPorts.IsIdentityMemberName(name);

    private IEnumerable<Site> CollectExitSites(SemanticModel model, SyntaxNode root, TenantProjectInput project, string relativePath)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (InvokedName(invocation.Expression) == "nameof")
            {
                continue;
            }

            if (model.GetOperation(invocation) is not IInvocationOperation operation)
            {
                if (IsPortName(InvokedName(invocation.Expression)))
                {
                    _analysis.Diagnostics.Add(BindFailure(project, relativePath, invocation, "invocation did not bind to an operation"));
                }

                continue;
            }

            var target = operation.TargetMethod.OriginalDefinition;
            if (target.ContainingType.TypeKind == TypeKind.Error)
            {
                if (IsPortName(target.Name))
                {
                    _analysis.Diagnostics.Add(BindFailure(project, relativePath, invocation, $"'{target.ContainingType.Name}' is an error type"));
                }

                continue;
            }

            foreach (var argument in operation.Arguments)
            {
                if (argument.Parameter is null || argument.Value.Syntax is not ExpressionSyntax expression)
                {
                    continue;
                }

                var port = TenantIdentitySymbols.ClassifyInvocationSlot(target, argument.Parameter);
                if (port is null)
                {
                    continue;
                }

                yield return new Site(expression, port.Value.Rule, port.Value.Label);
            }
        }

        foreach (var creation in CollectCreations(model, root))
        {
            if (creation.Created is null)
            {
                if (IsTenantIdentityTypeName(creation.TypeName))
                {
                    _analysis.Diagnostics.Add(new TenantIdentityDiagnostic(
                        TenantIdentityRule.TI900, project.Project, relativePath, 0, 0, creation.TypeName, "-",
                        "coverage failure: the created tenant identity type did not bind"));
                }

                continue;
            }

            if (TenantIdentitySymbols.FullName(creation.Created) == TenantIdentityPorts.ClaimType
                && creation.Constructor is not null)
            {
                // Claim's argument order is not guaranteed: resolve the type/value slots by parameter symbol so
                // reordered named arguments cannot move the identity value into another slot.
                var typeParameter = creation.Constructor.Parameters.FirstOrDefault(parameter => parameter.Name == "type");
                var valueParameter = creation.Constructor.Parameters.FirstOrDefault(parameter => parameter.Name == "value");
                if (typeParameter is not null
                    && valueParameter is not null
                    && creation.Arguments.TryGetValue(typeParameter.Ordinal, out var claimTypeExpression)
                    && creation.Arguments.TryGetValue(valueParameter.Ordinal, out var claimValueExpression)
                    && IsTenantClaimType(claimTypeExpression, model))
                {
                    yield return new Site(claimValueExpression, TenantIdentityRule.TI001, "Claim(tenantid|tenant_id) value slot");
                }
            }

            if (TenantIdentitySymbols.ImplementsTenantInfo(creation.Created) && creation.Constructor is not null)
            {
                foreach (var parameter in creation.Constructor.Parameters)
                {
                    if (TenantIdentitySymbols.IsIdentityConstructorParameter(parameter)
                        && creation.Arguments.TryGetValue(parameter.Ordinal, out var expression))
                    {
                        yield return new Site(
                            expression,
                            TenantIdentityRule.TI003,
                            $"{TenantIdentitySymbols.FullName(creation.Created)}.{parameter.Name} constructor id slot");
                    }
                }
            }
        }

        foreach (var comparison in root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                     .Where(node => node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression)))
        {
            var leftIsIdentity = TenantIdentitySymbols.IsTenantIdMember(model, comparison.Left);
            var rightIsIdentity = TenantIdentitySymbols.IsTenantIdMember(model, comparison.Right);

            if (leftIsIdentity && !rightIsIdentity)
            {
                yield return new Site(comparison.Right, TenantIdentityRule.TI002, "entity tenant filter comparison");
            }
            else if (rightIsIdentity && !leftIsIdentity)
            {
                yield return new Site(comparison.Left, TenantIdentityRule.TI002, "entity tenant filter comparison");
            }
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                     .Where(node => node.IsKind(SyntaxKind.SimpleAssignmentExpression)))
        {
            if (TenantIdentitySymbols.IsTenantIdMember(model, assignment.Left))
            {
                // Writing the ITenantInfo id member is identity masquerade (TI003); writing a tenant filter or
                // partition member is isolation (TI002).
                var rule = TenantIdentitySymbols.IsTenantInfoIdExpression(model, assignment.Left)
                    ? TenantIdentityRule.TI003
                    : TenantIdentityRule.TI002;
                yield return new Site(
                    assignment.Right,
                    rule,
                    rule == TenantIdentityRule.TI003 ? "ITenantInfo.Id identity write" : "tenant identity member write");
            }
            else if (IsIdentityName(model.GetSymbolInfo(assignment.Left).Symbol?.Name ?? string.Empty))
            {
                yield return new Site(assignment.Right, TenantIdentityRule.TI002, "tenant identity member write");
            }
        }

        foreach (var initializer in root.DescendantNodes().OfType<EqualsValueClauseSyntax>())
        {
            var (name, rule, label) = initializer.Parent switch
            {
                PropertyDeclarationSyntax property => (property.Identifier.ValueText, TenantIdentityRule.TI002, "tenant identity member initializer"),
                VariableDeclaratorSyntax declarator => (declarator.Identifier.ValueText, TenantIdentityRule.TI003, "identity-named local initialized from a value"),
                _ => (string.Empty, TenantIdentityRule.TI002, string.Empty),
            };

            if (IsIdentityName(name))
            {
                yield return new Site(initializer.Value, rule, label);
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (property.ExpressionBody is null || model.GetDeclaredSymbol(property) is not IPropertySymbol symbol)
            {
                continue;
            }

            var isIdentity = IsIdentityName(symbol.Name)
                || (symbol.Name == "Id" && TenantIdentitySymbols.ImplementsTenantInfo(symbol.ContainingType));
            if (!isIdentity)
            {
                continue;
            }

            yield return new Site(
                property.ExpressionBody.Expression,
                TenantIdentityRule.TI003,
                $"{TenantIdentitySymbols.FullName(symbol.ContainingType)}.{symbol.Name} identity getter");
        }

        foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            if (accessor.Parent is not (PropertyDeclarationSyntax or IndexerDeclarationSyntax))
            {
                continue;
            }

            var propertySymbol = model.GetDeclaredSymbol(accessor.Parent!) as IPropertySymbol;
            if (propertySymbol is null)
            {
                continue;
            }

            var isIdentity = IsIdentityName(propertySymbol.Name)
                || (propertySymbol.Name == "Id" && TenantIdentitySymbols.ImplementsTenantInfo(propertySymbol.ContainingType));
            if (!isIdentity)
            {
                continue;
            }

            var returned = accessor.ExpressionBody?.Expression
                ?? accessor.Body?.DescendantNodes().OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression;
            if (returned is not null)
            {
                yield return new Site(returned, TenantIdentityRule.TI003, $"{TenantIdentitySymbols.FullName(propertySymbol.ContainingType)}.{propertySymbol.Name} identity getter");
            }
        }

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol || !IsIdentityName(symbol.Name))
            {
                continue;
            }

            var returns = (method.ExpressionBody?.Expression is { } expression
                    ? [expression]
                    : method.DescendantNodes().OfType<ReturnStatementSyntax>().Select(statement => statement.Expression))
                .Where(node => node is not null)
                .Cast<ExpressionSyntax>();

            foreach (var returned in returns)
            {
                yield return new Site(returned, TenantIdentityRule.TI003, $"{TenantIdentitySymbols.FullName(symbol.ContainingType)}.{symbol.Name}() return slot");
            }
        }
    }

    private static string InvokedName(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => string.Empty,
    };

    private static string TerminalName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => string.Empty,
    };

    private static bool IsPortName(string name)
    {
        var normalized = TenantIdentityPorts.Normalize(name);
        return TenantIdentityPorts.ExitMemberTokens.Contains(normalized, StringComparer.Ordinal);
    }

    private static bool IsTenantIdentityTypeName(string name)
    {
        var normalized = TenantIdentityPorts.Normalize(name);
        return normalized.Contains("tenantinfo", StringComparison.Ordinal)
            || normalized.Contains("currenttenant", StringComparison.Ordinal)
            || normalized.Contains("tenantcontext", StringComparison.Ordinal);
    }

    /// <summary>
    /// Both <c>new Type(...)</c> and <c>new(...)</c> forms, with arguments bound to their parameter symbols so
    /// named and reordered arguments cannot land in the wrong slot.
    /// </summary>
    private static IEnumerable<Creation> CollectCreations(SemanticModel model, SyntaxNode root)
    {
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var operation = model.GetOperation(creation) as IObjectCreationOperation;
            var constructor = operation?.Constructor?.OriginalDefinition
                ?? model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
            var created = constructor?.ContainingType
                ?? model.GetSymbolInfo(creation.Type).Symbol as INamedTypeSymbol;
            yield return new Creation(created, constructor, ArgumentMap(operation?.Arguments), creation.Type.ToString());
        }

        foreach (var creation in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            var operation = model.GetOperation(creation) as IObjectCreationOperation;
            var constructor = operation?.Constructor?.OriginalDefinition
                ?? model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
            yield return new Creation(constructor?.ContainingType, constructor, ArgumentMap(operation?.Arguments), string.Empty);
        }
    }

    private static IReadOnlyDictionary<int, ExpressionSyntax> ArgumentMap(IEnumerable<IArgumentOperation>? arguments)
    {
        var map = new Dictionary<int, ExpressionSyntax>();
        foreach (var argument in arguments ?? [])
        {
            if (argument.Parameter is not null && argument.Value.Syntax is ExpressionSyntax expression)
            {
                map[argument.Parameter.Ordinal] = expression;
            }
        }

        return map;
    }

    private static bool IsTenantClaimType(ExpressionSyntax expression, SemanticModel model)
    {
        var constant = model.GetConstantValue(expression);
        if (constant.HasValue && constant.Value is string literal)
        {
            return TenantIdentityPorts.TenantClaimTypes.Contains(TenantIdentityPorts.Normalize(literal), StringComparer.Ordinal);
        }

        return TenantIdentityPorts.TenantClaimTypes.Any(claim =>
            expression.ToString().Contains($"\"{claim}\"", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record Creation(
        INamedTypeSymbol? Created,
        IMethodSymbol? Constructor,
        IReadOnlyDictionary<int, ExpressionSyntax> Arguments,
        string TypeName);

    private TenantIdentityDiagnostic BindFailure(TenantProjectInput project, string relativePath, SyntaxNode node, string reason)
    {
        var position = node.GetLocation().GetLineSpan().StartLinePosition;
        return new TenantIdentityDiagnostic(
            TenantIdentityRule.TI900,
            project.Project,
            relativePath,
            position.Line + 1,
            position.Character + 1,
            Collapse(node.ToString()),
            "-",
            $"coverage failure: {reason}");
    }

    private sealed record Write(ISymbol Symbol, ExpressionSyntax? Expression, bool IsIdentity, string Name);

    private sealed record Site(ExpressionSyntax Expression, TenantIdentityRule Rule, string Label);

}
