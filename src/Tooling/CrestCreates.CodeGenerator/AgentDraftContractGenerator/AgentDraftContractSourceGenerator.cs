using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace CrestCreates.CodeGenerator.AgentDraftContractGenerator;

[Generator]
public sealed class AgentDraftContractSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all classes with [AgentDraftContractSpec] attribute
        var specClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetSpecClassInfo(ctx))
            .Where(static spec => spec is not null);

        var specsAndCompilation = specClasses.Collect().Combine(context.CompilationProvider);

        context.RegisterSourceOutput(specsAndCompilation, static (sourceContext, pair) =>
        {
            var (specs, compilation) = pair;
            GenerateContractCode(sourceContext, compilation, specs!);
        });
    }

    private static SpecClassInfo? GetSpecClassInfo(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol is null) return null;

        // Check for [AgentDraftContractSpec] attribute by full name
        var specAttr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "CrestCreates.Agent.DraftContracts.Specs.AgentDraftContractSpecAttribute");

        if (specAttr is null) return null;

        // Extract Kind from attribute's named argument
        var kindArg = specAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "Kind");
        if (kindArg.Value.Kind == TypedConstantKind.Error) return null;

        string kindName;
        if (kindArg.Value.Kind == TypedConstantKind.Enum)
        {
            // Enum values are stored as underlying integers; map back to member name
            var enumType = kindArg.Value.Type as INamedTypeSymbol;
            var enumValue = (int)kindArg.Value.Value!;
            kindName = enumType?.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(m => m is { HasConstantValue: true } && (int)m.ConstantValue! == enumValue)?
                .Name ?? "Unknown";
        }
        else
        {
            kindName = kindArg.Value.Value?.ToString() ?? "Unknown";
        }

        if (string.IsNullOrEmpty(kindName) || kindName == "Unknown") return null;

        return new SpecClassInfo(symbol, kindName);
    }

    private static void GenerateContractCode(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<SpecClassInfo> specs)
    {
        if (specs.IsDefaultOrEmpty) return;

        var modelBuilder = new ContractModelBuilder(compilation, context);
        var model = modelBuilder.Build(specs);

        if (model is null || model.Kinds.Count == 0) return;

        if (context.CancellationToken.IsCancellationRequested) return;

        // Generate DTOs
        var dtoWriter = new AgentDraftContractDtoWriter();
        context.AddSource("AgentDraftPayloadDtos.g.cs", SourceText.From(dtoWriter.WritePayloadDtos(model), Encoding.UTF8));
        context.AddSource("AgentDraftPayloadPatchDtos.g.cs", SourceText.From(dtoWriter.WritePatchDtos(model), Encoding.UTF8));
        context.AddSource("AgentDraftChangedFieldEnums.g.cs", SourceText.From(dtoWriter.WriteChangedFieldEnums(model), Encoding.UTF8));

        // Generate projection helpers
        var projectionWriter = new AgentDraftContractProjectionWriter();
        context.AddSource("AgentDraftPayloadProjection.g.cs", SourceText.From(projectionWriter.Write(model), Encoding.UTF8));

        // Generate manifest
        var manifestWriter = new AgentDraftContractManifestWriter();
        context.AddSource("AgentDraftContractManifest.g.cs", SourceText.From(manifestWriter.Write(model), Encoding.UTF8));
    }
}
