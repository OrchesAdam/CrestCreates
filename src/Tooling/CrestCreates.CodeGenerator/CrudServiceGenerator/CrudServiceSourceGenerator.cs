using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using CrestCreates.Tooling.CrudGeneration;

namespace CrestCreates.CodeGenerator.CrudServiceGenerator
{
    [Generator]
    public class CrudServiceSourceGenerator : IIncrementalGenerator
    {
        private static readonly SymbolDisplayFormat FullyQualifiedFormat =
            SymbolDisplayFormat.FullyQualifiedFormat;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var entityClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsEntityCandidate(node),
                    transform: static (ctx, _) => GetEntityClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            // The CRUD precompile + JSON transport marker decides whether a formal declaration is
            // allowed to exist at all; the analyzer never falls back to the legacy surface.
            var toolchainAssembled = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(
                    "build_property.CrestCreatesCrudJsonToolchain", out var value) && value == "true");

            var ownedArtifacts = context.AdditionalTextsProvider.Collect()
                .Select(static (texts, _) => CrudArtifactOwnership.Read(texts))
                .WithComparer(CrudArtifactOwnership.ContentComparer);

            var compilationAndClasses = context.CompilationProvider
                .Combine(entityClasses)
                .Combine(toolchainAssembled.Combine(ownedArtifacts));

            context.RegisterSourceOutput(compilationAndClasses, ExecuteGeneration);
        }

        private static bool IsEntityCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0;
        }

        private static (INamedTypeSymbol Symbol, bool GenerateAsBaseClass, bool IsUsingNewAttribute)? GetEntityClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol != null && HasGenerateCrudServiceAttribute(symbol))
            {
                var isUsingNewAttribute = IsUsingGenerateEntityAttribute(symbol);
                var generateAsBaseClass = isUsingNewAttribute ?
                    GetAttributeBooleanValue(symbol, "GenerateAsBaseClass", false) :
                    false;
                return (symbol, generateAsBaseClass, isUsingNewAttribute);
            }

            return null;
        }

        private static bool IsUsingGenerateEntityAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "GenerateEntityAttribute" ||
                    attr.AttributeClass.Name == "GenerateEntity" ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateEntityAttribute") ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateEntity")
                ));
        }

        private static bool HasGenerateCrudServiceAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "GenerateCrudServiceAttribute" ||
                    attr.AttributeClass.Name == "GenerateCrudService" ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateCrudServiceAttribute") ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateCrudService")
                ));
        }

        /// <summary>
        /// 获取特性的布尔值
        /// </summary>
        private static bool GetAttributeBooleanValue(INamedTypeSymbol symbol, string propertyName, bool defaultValue)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass == null) continue;
                var name = attr.AttributeClass.Name;
                var fullName = attr.AttributeClass.ToDisplayString();
                if (!(name == "GenerateCrudServiceAttribute" || name == "GenerateCrudService" ||
                      name == "GenerateEntityAttribute" || name == "GenerateEntity" ||
                      fullName.EndsWith(".GenerateCrudServiceAttribute") || fullName.EndsWith(".GenerateCrudService") ||
                      fullName.EndsWith(".GenerateEntityAttribute") || fullName.EndsWith(".GenerateEntity")))
                    continue;

                var namedArgument = attr.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
                if (namedArgument.Value.Value != null && namedArgument.Value.Value is bool value)
                {
                    return value;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取特性的字符串数组值
        /// </summary>
        /// <summary>
        /// 获取特性的字符串值
        /// </summary>
        private void ExecuteGeneration(
            SourceProductionContext context,
            ((Compilation Compilation, ImmutableArray<(INamedTypeSymbol Symbol, bool GenerateAsBaseClass, bool IsUsingNewAttribute)?> Classes) Combined, (bool ToolchainAssembled, ImmutableSortedSet<string> OwnedArtifacts) State) input)
        {
            var (combined, state) = input;
            var (toolchainAssembled, ownedArtifacts) = state;
            var (compilation, entityClasses) = combined;
            if (entityClasses.IsDefaultOrEmpty) return;

            // #128 S2: a formal [GenerateCrudService(typeof(Entity))] declaration is materialized by
            // the CRUD precompile stage as ordinary Compile input. This generator keeps only the
            // declaration and assembly-consistency diagnostics for it; emitting a second surface here
            // is exactly the double track the design closes.
            var formalDeclarations = entityClasses
                .Where(info => info.HasValue)
                .Select(info => info!.Value.Symbol)
                .Where(IsFormalDeclaration)
                .ToImmutableHashSet(SymbolEqualityComparer.Default);
            if (!formalDeclarations.IsEmpty)
            {
                ReportFormalDeclarationDiagnostics(context, compilation, formalDeclarations.Count, toolchainAssembled, ownedArtifacts);
            }

            var processedLegacyDeclarations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entityInfo in entityClasses)
            {
                if (!entityInfo.HasValue)
                {
                    continue;
                }

                var legacyType = entityInfo.Value.Symbol;
                if (formalDeclarations.Contains(legacyType)
                    || !processedLegacyDeclarations.Add(legacyType.ToDisplayString()))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    CrudServiceDiagnostics.InvalidDeclaration,
                    Location.None,
                    $"The parameterless [GenerateCrudService] stub on '{legacyType.ToDisplayString()}' is retired; "
                    + "declare [GenerateCrudService(typeof(TEntity))] so the formal precompile/JSON toolchain owns the surface."));
            }
        }

        /// <summary>
        /// Formal recognition is by the attribute's metadata full name and symbol identity; the test
        /// stub with the same short name is not a delivered contract (§5.1).
        /// </summary>
        /// <summary>
        /// Formal recognition is by the attribute's metadata full name and symbol identity, plus the
        /// delivered <c>typeof(Entity)</c> constructor shape (§5.1). The test stub shares the full name
        /// but has no entity-typed constructor argument, so it stays on the legacy path and is never
        /// mistaken for the delivered contract.
        /// </summary>
        private static bool IsFormalDeclaration(INamedTypeSymbol type) => type.GetAttributes().Any(data =>
            data.AttributeClass?.ToDisplayString() == CrudGenerationModelBuilder.AttributeFullName
            && data.ConstructorArguments.Length == 1
            && data.ConstructorArguments[0].Value is INamedTypeSymbol);

        private static void ReportFormalDeclarationDiagnostics(
            SourceProductionContext context,
            Compilation compilation,
            int formalDeclarationCount,
            bool toolchainAssembled,
            ImmutableSortedSet<string> ownedArtifacts)
        {
            var build = CrudGenerationModelBuilder.Build(compilation, compilation.AssemblyName ?? string.Empty);
            foreach (var issue in build.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MapDescriptor(issue.Id),
                    Location.None,
                    issue.Message));
            }

            var actualSurface = build.Models.All(model =>
                CrudArtifactEmitter.Emit(model)
                    .SelectMany(artifact => CrudArtifactEmitter.DeclaredTypeFullNames(artifact.Source))
                    .All(name => ownedArtifacts.Contains(name)
                        && compilation.GetTypeByMetadataName(name) is INamedTypeSymbol type
                        && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)));
            if ((!toolchainAssembled || !actualSurface) && formalDeclarationCount > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CrudServiceDiagnostics.PrecompileTransportMissing,
                    Location.None,
                    $"{formalDeclarationCount} formal CRUD declaration(s) require the precompile/JSON toolchain, a valid ownership manifest and current materialized Compile artifacts."));
            }
        }

        private static DiagnosticDescriptor MapDescriptor(string id) => id switch
        {
            CrudDiagnosticIds.DeclarationUnsupported => CrudServiceDiagnostics.InvalidDeclaration,
            CrudDiagnosticIds.ToolchainNotAssembled => CrudServiceDiagnostics.PrecompileTransportMissing,
            CrudDiagnosticIds.OwnerIdentityConflict => CrudServiceDiagnostics.IdentityConflict,
            CrudDiagnosticIds.StaticClosureUnsupported => CrudServiceDiagnostics.StaticClosureUnsupported,
            CrudDiagnosticIds.RequiredReferenceMissing => CrudServiceDiagnostics.MissingRequiredReference,
            CrudDiagnosticIds.PrecompileStateInvalid => CrudServiceDiagnostics.PrecompileInputCorrupted,
            CrudDiagnosticIds.QueryConfigurationUnsupported => CrudServiceDiagnostics.FilterSortUnsupported,
            CrudDiagnosticIds.LegacyContractMismatch => CrudServiceDiagnostics.LegacySerializationInconsistent,
            _ => CrudServiceDiagnostics.InvalidDeclaration,
        };
    }
}
