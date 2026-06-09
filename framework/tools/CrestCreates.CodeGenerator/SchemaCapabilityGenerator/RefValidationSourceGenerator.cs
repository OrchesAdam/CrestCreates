using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.SchemaCapabilityGenerator;

[Generator]
public sealed class RefValidationSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var refs = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsVersionedDescriptorRef(node),
                transform: static (ctx, ct) => ExtractRef(ctx))
            .Where(static x => x is not null)
            .Collect();

        var compilationProvider = context.CompilationProvider;

        context.RegisterSourceOutput(
            refs.Combine(compilationProvider),
            static (spc, source) =>
            {
                ValidateReferences(spc, source.Left, source.Right);
            });
    }

    private static bool IsVersionedDescriptorRef(SyntaxNode node)
    {
        return node is ObjectCreationExpressionSyntax creation
            && creation.Type is GenericNameSyntax generic
            && generic.Identifier.Text == "VersionedDescriptorRef";
    }

    private static DescriptorRefInfo? ExtractRef(GeneratorSyntaxContext ctx)
    {
        var creation = (ObjectCreationExpressionSyntax)ctx.Node;
        var generic = (GenericNameSyntax)creation.Type;

        var idArg = creation.ArgumentList?.Arguments.FirstOrDefault();
        if (idArg == null) return null;

        var idValue = ctx.SemanticModel.GetConstantValue(idArg.Expression);
        if (!idValue.HasValue || idValue.Value is not string id) return null;

        var versionArg = creation.ArgumentList!.Arguments.Skip(1).FirstOrDefault();
        int? version = null;
        if (versionArg != null)
        {
            var v = ctx.SemanticModel.GetConstantValue(versionArg.Expression);
            if (v.HasValue && v.Value is int ver) version = ver;
        }

        var typeArg = generic.TypeArgumentList.Arguments.FirstOrDefault();

        return new DescriptorRefInfo
        {
            Id = id,
            Version = version,
            DescriptorType = typeArg?.ToString() ?? "Unknown",
            Location = creation.GetLocation()
        };
    }

    private static void ValidateReferences(
        SourceProductionContext spc,
        ImmutableArray<DescriptorRefInfo?> refs,
        Compilation compilation)
    {
        var validRefs = refs.Where(r => r != null).Select(r => r!).ToList();
        if (validRefs.Count == 0) return;

        var knownIds = CollectKnownDescriptorIds(compilation);

        foreach (var descriptorRef in validRefs)
        {
            if (!knownIds.Contains(descriptorRef.Id))
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "CC1001",
                        "Unresolved descriptor reference",
                        "VersionedDescriptorRef<{0}> references descriptor '{1}' which is not registered in any registry. Ensure the descriptor exists and is registered via a DescriptorProvider or source generator.",
                        "DescriptorValidation",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    descriptorRef.Location,
                    descriptorRef.DescriptorType,
                    descriptorRef.Id);

                spc.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static HashSet<string> CollectKnownDescriptorIds(Compilation compilation)
    {
        var ids = new HashSet<string>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var strings = root.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression));

            foreach (var str in strings)
            {
                var value = str.Token.ValueText;
                if (IsDescriptorId(value))
                    ids.Add(value);
            }
        }

        return ids;
    }

    private static bool IsDescriptorId(string value)
    {
        return value.StartsWith("schema_")
            || value.StartsWith("cap_")
            || value.StartsWith("evt_")
            || value.StartsWith("form_")
            || value.StartsWith("ht_")
            || value.StartsWith("wf_");
    }
}

internal sealed class DescriptorRefInfo
{
    public string Id { get; set; } = string.Empty;
    public int? Version { get; set; }
    public string DescriptorType { get; set; } = string.Empty;
    public Location Location { get; set; } = null!;
}
