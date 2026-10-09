using System;
using System.Collections.Generic;
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
public sealed class OutboxConsumerActivationSourceGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivationAttribute";
    private const string AttributeShortName = "GenerateOutboxConsumerActivationAttribute";
    private const string ActivationInterfaceName = "IOutboxConsumerActivation";
    private const string ActivationInterfaceNamespace = "CrestCreates.Runtime.Delivery.Abstractions.Activation";
    private const string RequiredConsumerName = "IOutboxRequiredConsumer";
    private const string RequiredConsumerNamespace = "CrestCreates.Runtime.Delivery.Abstractions.Handlers";

    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor CCOCA001_NonPartialOrUnsupportedShape = new(
            id: "CCOCA001",
            title: "Unsupported type shape for outbox consumer activation",
            messageFormat: "Type '{0}' must be a partial class (not a record, struct, or interface) to use [GenerateOutboxConsumerActivation]",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CCOCA002_DoesNotImplementRequiredConsumer = new(
            id: "CCOCA002",
            title: "Type does not implement IOutboxRequiredConsumer<T>",
            messageFormat: "Type '{0}' must implement IOutboxRequiredConsumer<T> to use [GenerateOutboxConsumerActivation]",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CCOCA003_NoOrMultiplePublicConstructors = new(
            id: "CCOCA003",
            title: "Expected exactly one public constructor",
            messageFormat: "Type '{0}' must have exactly one public constructor, but found {1}",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CCOCA004_UnsupportedParameterType = new(
            id: "CCOCA004",
            title: "Unsupported constructor parameter type",
            messageFormat: "Constructor parameter '{0}' of type '{1}' is not supported (IServiceProvider, keyed, ref, out, and in parameters are disallowed)",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CCOCA005_RequiredMembersNotSatisfied = new(
            id: "CCOCA005",
            title: "Required members not satisfied",
            messageFormat: "Type '{0}' has required member(s) '{1}' that cannot be satisfied by the constructor",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CCOCA006_HandWrittenActivationExists = new(
            id: "CCOCA006",
            title: "Hand-written activation already exists",
            messageFormat: "Type '{0}' already has a hand-written CreateOutboxConsumer method; remove it or remove [GenerateOutboxConsumerActivation]",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CCOCA007_MissingActivationContract = new(
            id: "CCOCA007",
            title: "Missing activation contract",
            messageFormat: "Type '{0}' must declare implementation of IOutboxConsumerActivation<{0}> to use [GenerateOutboxConsumerActivation]",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Match class declarations with attributes (candidates)
        var classProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsClassCandidate(node),
                transform: static (ctx, ct) => AnalyzeType(ctx, ct))
            .Where(static m => m is not null);

        // Match record declarations to report CCOCA001
        var recordProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsRecordCandidate(node),
                transform: static (ctx, ct) => AnalyzeRecordForDiagnostic(ctx, ct))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(classProvider.Collect(), static (spc, models) => ExecuteGeneration(spc, models));
        context.RegisterSourceOutput(recordProvider, static (spc, diagnostic) =>
        {
            if (diagnostic is not null)
                spc.ReportDiagnostic(diagnostic);
        });
    }

    private static bool IsClassCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl && classDecl.AttributeLists.Count > 0;
    }

    private static bool IsRecordCandidate(SyntaxNode node)
    {
        return node is RecordDeclarationSyntax recordDecl && recordDecl.AttributeLists.Count > 0;
    }

    private static Diagnostic? AnalyzeRecordForDiagnostic(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var recordDecl = (RecordDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(recordDecl, ct);
        if (symbol is null) return null;

        var hasAttribute = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFullName ||
            a.AttributeClass?.Name == AttributeShortName);

        if (!hasAttribute) return null;

        return Diagnostic.Create(
            DiagnosticDescriptors.CCOCA001_NonPartialOrUnsupportedShape,
            recordDecl.GetLocation(),
            symbol.Name);
    }

    private static ConsumerActivationModel? AnalyzeType(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
        if (symbol is null) return null;

        var hasAttribute = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFullName ||
            a.AttributeClass?.Name == AttributeShortName);

        if (!hasAttribute) return null;

        var model = new ConsumerActivationModel
        {
            TypeName = symbol.Name,
            TypeFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace = symbol.ContainingNamespace.ToDisplayString(),
            Location = classDecl.GetLocation(),
            Accessibility = symbol.DeclaredAccessibility
        };

        // CCOCA001: Must be a partial class
        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA001_NonPartialOrUnsupportedShape,
                classDecl.GetLocation(),
                symbol.Name));
            return model;
        }

        // CCOCA007: Must declare IOutboxConsumerActivation<TSelf>
        var declaresActivationInterface = symbol.AllInterfaces.Any(i =>
            i.IsGenericType &&
            i.Arity == 1 &&
            i.Name == ActivationInterfaceName &&
            i.ContainingNamespace?.ToDisplayString() == ActivationInterfaceNamespace);

        if (!declaresActivationInterface)
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA007_MissingActivationContract,
                classDecl.GetLocation(),
                symbol.Name));
            return model;
        }

        // CCOCA002: Must implement IOutboxRequiredConsumer<T>
        var implementsRequiredConsumer = symbol.AllInterfaces.Any(i =>
            i.IsGenericType &&
            i.Arity == 1 &&
            i.Name == RequiredConsumerName &&
            i.ContainingNamespace?.ToDisplayString() == RequiredConsumerNamespace);

        if (!implementsRequiredConsumer)
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA002_DoesNotImplementRequiredConsumer,
                classDecl.GetLocation(),
                symbol.Name));
            return model;
        }

        // CCOCA006: Check for hand-written CreateOutboxConsumer
        var existingMethod = symbol.GetMembers("CreateOutboxConsumer").OfType<IMethodSymbol>().FirstOrDefault();
        if (existingMethod is not null)
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA006_HandWrittenActivationExists,
                classDecl.GetLocation(),
                symbol.Name));
            return model;
        }

        // CCOCA003: Must have exactly one public constructor
        var publicConstructors = symbol.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        if (publicConstructors.Length != 1)
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA003_NoOrMultiplePublicConstructors,
                classDecl.GetLocation(),
                symbol.Name,
                publicConstructors.Length));
            return model;
        }

        var ctor = publicConstructors[0];

        // CCOCA005: Check for required members
        // In netstandard2.0 Roslyn, ISymbol.IsRequired is not available.
        // Check for required members via the RequiredMemberAttribute.
        var requiredMembers = symbol.GetMembers()
            .Where(m => m.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "RequiredMemberAttribute" ||
                a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.RequiredMemberAttribute"))
            .Select(m => m.Name)
            .ToArray();

        if (requiredMembers.Length > 0)
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA005_RequiredMembersNotSatisfied,
                classDecl.GetLocation(),
                symbol.Name,
                string.Join(", ", requiredMembers)));
            return model;
        }

        // CCOCA004: Check constructor parameters
        foreach (var param in ctor.Parameters)
        {
            // Disallow IServiceProvider
            if (param.Type.ToDisplayString() == "System.IServiceProvider" ||
                param.Type.AllInterfaces.Any(i => i.ToDisplayString() == "System.IServiceProvider"))
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA004_UnsupportedParameterType,
                    param.Locations.FirstOrDefault() ?? classDecl.GetLocation(),
                    param.Name,
                    param.Type.ToDisplayString()));
                return model;
            }

            // Disallow ref, out, in
            if (param.RefKind != RefKind.None)
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA004_UnsupportedParameterType,
                    param.Locations.FirstOrDefault() ?? classDecl.GetLocation(),
                    param.Name,
                    param.Type.ToDisplayString()));
                return model;
            }

            // Disallow keyed services (check for [FromKeyedServices] attribute)
            var hasKeyedAttr = param.GetAttributes().Any(a =>
                a.AttributeClass?.Name.Contains("Keyed") == true ||
                a.AttributeClass?.ToDisplayString()?.Contains("FromKeyedServices") == true);
            if (hasKeyedAttr)
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA004_UnsupportedParameterType,
                    param.Locations.FirstOrDefault() ?? classDecl.GetLocation(),
                    param.Name,
                    param.Type.ToDisplayString()));
                return model;
            }
        }

        // All validations passed - build the parameter info
        model.IsValid = true;
        foreach (var param in ctor.Parameters)
        {
            model.Parameters.Add(new ConstructorParameterInfo
            {
                Name = param.Name,
                TypeFullName = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            });
        }

        return model;
    }

    private static void ExecuteGeneration(SourceProductionContext context, ImmutableArray<ConsumerActivationModel?> models)
    {
        if (models.IsDefaultOrEmpty) return;

        var validModels = new List<ConsumerActivationModel>();

        foreach (var model in models)
        {
            if (model is null) continue;

            // Report all diagnostics
            foreach (var diagnostic in model.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            if (model.IsValid)
            {
                validModels.Add(model);
            }
        }

        if (validModels.Count == 0) return;

        foreach (var model in validModels)
        {
            var source = GenerateActivationCode(model);
            var hintName = $"{model.TypeName}.OutboxConsumerActivation.g.cs";
            context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string GenerateActivationCode(ConsumerActivationModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrestCreates.CodeGenerator.OutboxConsumerActivationGenerator");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        var accessibility = model.Accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "internal"
        };

        sb.AppendLine($"{accessibility} partial class {model.TypeName}");
        sb.AppendLine("{");
        sb.AppendLine($"    static {model.TypeFullName} global::CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation<{model.TypeFullName}>.CreateOutboxConsumer(global::System.IServiceProvider services)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return new {model.TypeFullName}(");

        for (var i = 0; i < model.Parameters.Count; i++)
        {
            var param = model.Parameters[i];
            var comma = i < model.Parameters.Count - 1 ? "," : "";
            sb.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{param.TypeFullName}>(services){comma}");
        }

        sb.AppendLine("        );");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}

internal sealed class ConsumerActivationModel
{
    public string TypeName { get; set; } = string.Empty;
    public string TypeFullName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public Location? Location { get; set; }
    public Accessibility Accessibility { get; set; }
    public bool IsValid { get; set; }
    public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();
    public List<ConstructorParameterInfo> Parameters { get; } = new List<ConstructorParameterInfo>();
}

internal sealed class ConstructorParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string TypeFullName { get; set; } = string.Empty;
}
