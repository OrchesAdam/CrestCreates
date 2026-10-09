using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.OutboxConsumerActivationGenerator;

[Generator]
public sealed class OutboxConsumerActivationGenerator : IIncrementalGenerator
{
    private const string MarkerAttributeMetadataName =
        "CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivationAttribute";

    private const string RequiredConsumerInterfaceMetadataName =
        "CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<T>";

    private const string ActivationInterfaceMetadataName =
        "CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation<TSelf>";

    private const string ServiceProviderMetadataName = "System.IServiceProvider";
    private const string ServiceScopeFactoryMetadataName = "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, ct) => Transform(ctx, ct))
            .Where(static x => x is not null)!;

        context.RegisterSourceOutput(results.Collect(), ExecuteGeneration);
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl && classDecl.AttributeLists.Count > 0;
    }

    private static GeneratorResult? Transform(
        GeneratorSyntaxContext context,
        CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is not INamedTypeSymbol typeSymbol)
            return null;

        var hasMarker = HasMarkerAttribute(typeSymbol);
        if (!hasMarker)
            return null;

        ct.ThrowIfCancellationRequested();

        var location = classDecl.Identifier.GetLocation();
        var displayName = typeSymbol.ToDisplayString();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (!ValidateShape(typeSymbol, classDecl, location, displayName, diagnostics))
            return new GeneratorResult(null, diagnostics.ToImmutable());

        if (!ValidateRequiredConsumerInterface(typeSymbol, location, displayName, diagnostics))
            return new GeneratorResult(null, diagnostics.ToImmutable());

        if (!ValidateNoManualActivationConflict(typeSymbol, location, displayName, diagnostics))
            return new GeneratorResult(null, diagnostics.ToImmutable());

        var constructor = SelectConstructor(typeSymbol, location, displayName, diagnostics);
        if (constructor is null)
            return new GeneratorResult(null, diagnostics.ToImmutable());

        var parameters = ValidateAndExtractParameters(constructor, typeSymbol, location, displayName, diagnostics);
        if (parameters is null)
            return new GeneratorResult(null, diagnostics.ToImmutable());

        if (!ValidateRequiredMembers(typeSymbol, constructor, location, displayName, diagnostics))
            return new GeneratorResult(null, diagnostics.ToImmutable());

        var accessibility = typeSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var hintName = BuildDeterministicHintName(typeSymbol);

        var model = new ConsumerActivationModel
        {
            Namespace = ns,
            ClassName = typeSymbol.Name,
            FullyQualifiedName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility = accessibility,
            ConstructorParameters = parameters.Value,
            HintName = hintName,
            HasZeroParameters = parameters.Value.IsEmpty
        };

        return new GeneratorResult(model, diagnostics.ToImmutable());
    }

    private static bool HasMarkerAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass != null &&
            string.Equals(a.AttributeClass.ToDisplayString(), MarkerAttributeMetadataName, System.StringComparison.Ordinal));
    }

    private static bool ValidateShape(
        INamedTypeSymbol typeSymbol,
        ClassDeclarationSyntax classDecl,
        Location location,
        string displayName,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (typeSymbol.IsAbstract)
        {
            diagnostics.Add(Diagnostic.Create(
                OutboxConsumerActivationDiagnostics.NotPartialOrUnsupportedShape,
                location, displayName));
            return false;
        }

        if (typeSymbol.IsGenericType || typeSymbol.TypeParameters.Length > 0)
        {
            diagnostics.Add(Diagnostic.Create(
                OutboxConsumerActivationDiagnostics.NotPartialOrUnsupportedShape,
                location, displayName));
            return false;
        }

        if (typeSymbol.ContainingType != null)
        {
            diagnostics.Add(Diagnostic.Create(
                OutboxConsumerActivationDiagnostics.NotPartialOrUnsupportedShape,
                location, displayName));
            return false;
        }

        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(
                OutboxConsumerActivationDiagnostics.NotPartialOrUnsupportedShape,
                location, displayName));
            return false;
        }

        return true;
    }

    private static bool ValidateRequiredConsumerInterface(
        INamedTypeSymbol typeSymbol,
        Location location,
        string displayName,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var implements = typeSymbol.AllInterfaces.Any(i =>
            i.Name == "IOutboxRequiredConsumer" &&
            i.ContainingNamespace?.ToDisplayString() == "CrestCreates.Runtime.Delivery.Abstractions.Handlers");

        if (!implements)
        {
            diagnostics.Add(Diagnostic.Create(
                OutboxConsumerActivationDiagnostics.NoRequiredConsumerInterface,
                location, displayName));
            return false;
        }

        return true;
    }

    private static bool ValidateNoManualActivationConflict(
        INamedTypeSymbol typeSymbol,
        Location location,
        string displayName,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var iface in typeSymbol.Interfaces)
        {
            if (iface.Name == "IOutboxConsumerActivation" &&
                iface.ContainingNamespace?.ToDisplayString() == "CrestCreates.Runtime.Delivery.Abstractions.Activation")
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.ManualActivationConflict,
                    location, displayName));
                return false;
            }
        }

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.IsStatic &&
                string.Equals(method.Name, "CreateOutboxConsumer", System.StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.ManualActivationConflict,
                    location, displayName));
                return false;
            }
        }

        return true;
    }

    private static IMethodSymbol? SelectConstructor(
        INamedTypeSymbol typeSymbol,
        Location location,
        string displayName,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var publicConstructors = typeSymbol.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToImmutableArray();

        if (publicConstructors.Length != 1)
        {
            diagnostics.Add(Diagnostic.Create(
                OutboxConsumerActivationDiagnostics.NoPublicConstructorOrMultiplePublicConstructors,
                location, displayName, publicConstructors.Length));
            return null;
        }

        return publicConstructors[0];
    }

    private static ImmutableArray<ConstructorParameterInfo>? ValidateAndExtractParameters(
        IMethodSymbol constructor,
        INamedTypeSymbol typeSymbol,
        Location location,
        string displayName,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var builder = ImmutableArray.CreateBuilder<ConstructorParameterInfo>();
        var hasErrors = false;

        foreach (var param in constructor.Parameters)
        {
            var paramType = param.Type;

            if (param.RefKind != RefKind.None)
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.UnsupportedParameterOrKeyedServiceProvider,
                    location, param.Name, displayName, "ref/out/in parameters are not supported"));
                hasErrors = true;
                continue;
            }

            if (param.IsParams)
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.UnsupportedParameterOrKeyedServiceProvider,
                    location, param.Name, displayName, "params parameters are not supported"));
                hasErrors = true;
                continue;
            }

            if (paramType.TypeKind == TypeKind.Pointer || paramType.TypeKind == TypeKind.FunctionPointer)
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.UnsupportedParameterOrKeyedServiceProvider,
                    location, param.Name, displayName, "pointer types are not supported"));
                hasErrors = true;
                continue;
            }

            if (paramType.TypeKind == TypeKind.Dynamic)
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.UnsupportedParameterOrKeyedServiceProvider,
                    location, param.Name, displayName, "dynamic parameters are not supported"));
                hasErrors = true;
                continue;
            }

            var typeDisplay = paramType.ToDisplayString();
            if (string.Equals(typeDisplay, ServiceProviderMetadataName, System.StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.UnsupportedParameterOrKeyedServiceProvider,
                    location, param.Name, displayName, "IServiceProvider is not allowed as a constructor dependency; use concrete service types"));
                hasErrors = true;
                continue;
            }

            if (string.Equals(typeDisplay, ServiceScopeFactoryMetadataName, System.StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.UnsupportedParameterOrKeyedServiceProvider,
                    location, param.Name, displayName, "IServiceScopeFactory is not allowed as a constructor dependency; use concrete service types"));
                hasErrors = true;
                continue;
            }

            if (param.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"))
            {
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.UnsupportedParameterOrKeyedServiceProvider,
                    location, param.Name, displayName, "keyed DI parameters are not supported"));
                hasErrors = true;
                continue;
            }

            builder.Add(new ConstructorParameterInfo
            {
                TypeName = paramType.ToDisplayString(),
                GlobalTypeName = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ParameterName = param.Name
            });
        }

        return hasErrors ? null : builder.ToImmutable();
    }

    private static bool ValidateRequiredMembers(
        INamedTypeSymbol typeSymbol,
        IMethodSymbol constructor,
        Location location,
        string displayName,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var hasRequiredMembers = false;
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IFieldSymbol field && field.IsRequired)
            {
                hasRequiredMembers = true;
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.RequiredMemberNotSatisfied,
                    location, displayName, field.Name));
            }
            else if (member is IPropertySymbol prop && prop.IsRequired)
            {
                hasRequiredMembers = true;
                diagnostics.Add(Diagnostic.Create(
                    OutboxConsumerActivationDiagnostics.RequiredMemberNotSatisfied,
                    location, displayName, prop.Name));
            }
        }

        return !hasRequiredMembers;
    }

    private static string BuildDeterministicHintName(INamedTypeSymbol typeSymbol)
    {
        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? "Global"
            : typeSymbol.ContainingNamespace.ToDisplayString().Replace('.', '_');
        return $"GeneratedOutboxConsumerActivation_{ns}_{typeSymbol.Name}.g.cs";
    }

    private static void ExecuteGeneration(
        SourceProductionContext context,
        ImmutableArray<GeneratorResult?> results)
    {
        if (results.IsDefaultOrEmpty)
            return;

        foreach (var result in results)
        {
            if (result is null)
                continue;

            foreach (var diag in result.Diagnostics)
                context.ReportDiagnostic(diag);

            if (result.Model is not null)
            {
                var source = EmitActivation(result.Model);
                context.AddSource(result.Model.HintName, SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    private static string EmitActivation(ConsumerActivationModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"{model.Accessibility} sealed partial class {model.ClassName}");
        sb.AppendLine($"    : global::CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation<{model.FullyQualifiedName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    static {model.FullyQualifiedName} global::CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation<{model.FullyQualifiedName}>.CreateOutboxConsumer(global::System.IServiceProvider services)");

        if (model.HasZeroParameters)
        {
            sb.AppendLine($"        => new {model.FullyQualifiedName}();");
        }
        else
        {
            sb.AppendLine($"        => new {model.FullyQualifiedName}(");
            for (var i = 0; i < model.ConstructorParameters.Length; i++)
            {
                var param = model.ConstructorParameters[i];
                var comma = i < model.ConstructorParameters.Length - 1 ? "," : "";
                sb.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{param.GlobalTypeName}>(services){comma}");
            }
            sb.AppendLine("        );");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private sealed class GeneratorResult
    {
        public GeneratorResult(ConsumerActivationModel? model, ImmutableArray<Diagnostic> diagnostics)
        {
            Model = model;
            Diagnostics = diagnostics;
        }

        public ConsumerActivationModel? Model { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
