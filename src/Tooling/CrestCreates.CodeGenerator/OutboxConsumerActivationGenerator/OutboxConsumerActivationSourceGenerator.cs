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
    private const string ActivationInterfaceName = "IOutboxConsumerActivation";
    private const string ActivationInterfaceFullName = "CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation";
    private const string RequiredConsumerName = "IOutboxRequiredConsumer";
    private const string RequiredConsumerNamespace = "CrestCreates.Runtime.Delivery.Abstractions.Handlers";
    private const string KeyedServicesAttributeFullName = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";

    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor CCOCA001_NonPartialOrUnsupportedShape = new(
            id: "CCOCA001",
            title: "Unsupported type shape for outbox consumer activation",
            messageFormat: "Type '{0}' must be a non-generic partial class (not a record, struct, interface, or open generic) to use [GenerateOutboxConsumerActivation]",
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
            messageFormat: "Constructor parameter '{0}' of type '{1}' is not supported (IServiceProvider, IServiceScopeFactory, keyed, ref, out, and in parameters are disallowed)",
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
            messageFormat: "Type '{0}' already has a hand-written CreateOutboxConsumer method or IOutboxConsumerActivation implementation; remove it or remove [GenerateOutboxConsumerActivation]",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CCOCA007_MissingActivationContract = new(
            id: "CCOCA007",
            title: "Missing activation contract",
            messageFormat: "Type '{0}' cannot be activated: the required compilation contract (IOutboxConsumerActivation<T> or IOutboxRequiredConsumer<T>) is missing or has a version mismatch; ensure CrestCreates.Runtime.Delivery.Abstractions references are aligned",
            category: "CrestCreates.Runtime.Delivery.Activation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsClassCandidate(node),
                transform: static (ctx, ct) => AnalyzeType(ctx, ct))
            .Where(static m => m is not null);

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

        if (!HasExactMarkerAttribute(symbol)) return null;

        return Diagnostic.Create(
            DiagnosticDescriptors.CCOCA001_NonPartialOrUnsupportedShape,
            recordDecl.GetLocation(),
            symbol.Name);
    }

    /// <summary>
    /// R3 fix: Only match the exact framework attribute by metadata name.
    /// No short-name fallback — prevents unrelated types with same-named attributes from being matched.
    /// </summary>
    private static bool HasExactMarkerAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass is not null &&
            a.AttributeClass.ToDisplayString() == AttributeFullName);
    }

    private static ConsumerActivationModel? AnalyzeType(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
        if (symbol is null) return null;

        // R3 fix: exact attribute match only
        if (!HasExactMarkerAttribute(symbol)) return null;

        var model = new ConsumerActivationModel
        {
            TypeName = symbol.Name,
            TypeFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            // R4 fix: use IsGlobalNamespace to detect global namespace
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(),
            IsGlobalNamespace = symbol.ContainingNamespace.IsGlobalNamespace,
            Location = classDecl.GetLocation(),
            Accessibility = symbol.DeclaredAccessibility,
            // R1 fix: use fully-qualified identity as dedup key
            SymbolIdentity = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };

        // CCOCA001: Must be a partial class, non-generic, top-level or nested but not open generic
        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA001_NonPartialOrUnsupportedShape,
                classDecl.GetLocation(),
                symbol.Name));
            return model;
        }

        if (symbol.IsGenericType || symbol.IsAbstract)
        {
            model.Diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CCOCA001_NonPartialOrUnsupportedShape,
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

        // R2 fix: Do NOT require business to manually declare IOutboxConsumerActivation<T>.
        // The generator will add it to the base list. But check if the type already
        // explicitly implements it (which would conflict with generated code).
        var alreadyDeclaresActivation = symbol.Interfaces.Any(i =>
            i.IsGenericType &&
            i.Arity == 1 &&
            i.Name == ActivationInterfaceName &&
            i.ContainingNamespace?.ToDisplayString() == "CrestCreates.Runtime.Delivery.Abstractions.Activation");

        if (alreadyDeclaresActivation)
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
        // P2 fix: Honor [SetsRequiredMembers] attribute on the constructor
        var requiredMembers = symbol.GetMembers()
            .Where(m => m.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "RequiredMemberAttribute" ||
                a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.RequiredMemberAttribute"))
            .Select(m => m.Name)
            .ToArray();

        if (requiredMembers.Length > 0)
        {
            // Check if the constructor has [SetsRequiredMembers]
            var hasSetsRequiredMembers = ctor.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "SetsRequiredMembersAttribute" ||
                a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");

            if (!hasSetsRequiredMembers)
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA005_RequiredMembersNotSatisfied,
                    classDecl.GetLocation(),
                    symbol.Name,
                    string.Join(", ", requiredMembers)));
                return model;
            }
        }

        // CCOCA004: Check constructor parameters
        foreach (var param in ctor.Parameters)
        {
            var paramTypeDisplay = param.Type.ToDisplayString();

            // Disallow IServiceProvider
            if (paramTypeDisplay == "System.IServiceProvider")
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA004_UnsupportedParameterType,
                    param.Locations.FirstOrDefault() ?? classDecl.GetLocation(),
                    param.Name,
                    paramTypeDisplay));
                return model;
            }

            // Disallow IServiceScopeFactory
            if (paramTypeDisplay == "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory")
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA004_UnsupportedParameterType,
                    param.Locations.FirstOrDefault() ?? classDecl.GetLocation(),
                    param.Name,
                    paramTypeDisplay));
                return model;
            }

            // Disallow ref, out, in
            if (param.RefKind != RefKind.None)
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA004_UnsupportedParameterType,
                    param.Locations.FirstOrDefault() ?? classDecl.GetLocation(),
                    param.Name,
                    paramTypeDisplay));
                return model;
            }

            // R3 fix: Disallow keyed services — exact attribute match, not substring
            var hasKeyedAttr = param.GetAttributes().Any(a =>
                a.AttributeClass is not null &&
                a.AttributeClass.ToDisplayString() == KeyedServicesAttributeFullName);
            if (hasKeyedAttr)
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.CCOCA004_UnsupportedParameterType,
                    param.Locations.FirstOrDefault() ?? classDecl.GetLocation(),
                    param.Name,
                    paramTypeDisplay));
                return model;
            }
        }

        // All validations passed
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

        // R1 fix: deduplicate by fully-qualified symbol identity
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var validModels = new List<ConsumerActivationModel>();

        foreach (var model in models)
        {
            if (model is null) continue;

            foreach (var diagnostic in model.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            if (model.IsValid && seen.Add(model.SymbolIdentity))
            {
                validModels.Add(model);
            }
        }

        if (validModels.Count == 0) return;

        foreach (var model in validModels)
        {
            var source = GenerateActivationCode(model);
            // R1 fix: hintName uses fully-qualified type identity encoding
            var hintName = BuildHintName(model);
            context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    /// <summary>
    /// P1 fix: Build a collision-free hint name from the fully-qualified type identity.
    /// Preserves dots as namespace separators to avoid A_B vs A.B collision.
    /// </summary>
    private static string BuildHintName(ConsumerActivationModel model)
    {
        var identity = model.SymbolIdentity
            .Replace("global::", "")
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_');
        return $"{identity}.OutboxConsumerActivation.g.cs";
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

        // R4 fix: only emit namespace block when not in global namespace
        if (!model.IsGlobalNamespace && !string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace}");
            sb.AppendLine("{");
        }

        var accessibility = model.Accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "internal"
        };

        var indent = (!model.IsGlobalNamespace && !string.IsNullOrEmpty(model.Namespace)) ? "    " : "";

        // R2 fix: generated partial adds IOutboxConsumerActivation<T> to base list
        sb.AppendLine($"{indent}{accessibility} partial class {model.TypeName}");
        sb.AppendLine($"{indent}    : global::CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation<{model.TypeFullName}>");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    static {model.TypeFullName} global::CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation<{model.TypeFullName}>.CreateOutboxConsumer(global::System.IServiceProvider services)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        return new {model.TypeFullName}(");

        for (var i = 0; i < model.Parameters.Count; i++)
        {
            var param = model.Parameters[i];
            var comma = i < model.Parameters.Count - 1 ? "," : "";
            sb.AppendLine($"{indent}            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{param.TypeFullName}>(services){comma}");
        }

        sb.AppendLine($"{indent}        );");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");

        if (!model.IsGlobalNamespace && !string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }
}

internal sealed class ConsumerActivationModel
{
    public string TypeName { get; set; } = string.Empty;
    public string TypeFullName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public bool IsGlobalNamespace { get; set; }
    public Location? Location { get; set; }
    public Accessibility Accessibility { get; set; }
    public bool IsValid { get; set; }
    /// <summary>R1 fix: fully-qualified symbol identity for deduplication.</summary>
    public string SymbolIdentity { get; set; } = string.Empty;
    public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();
    public List<ConstructorParameterInfo> Parameters { get; } = new List<ConstructorParameterInfo>();
}

internal sealed class ConstructorParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string TypeFullName { get; set; } = string.Empty;
}
