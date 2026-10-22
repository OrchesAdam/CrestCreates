using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CrestCreates.CodeGenerator.AppServiceHttpProjection;

/// <summary>
/// Default-toolchain guard for hand-written AppService HTTP projections. The formal projection is
/// materialized by the AppService HTTP/JSON BuildTasks chain, which is a separate import; a project
/// that declares a projection without that chain previously built successfully with no endpoint.
/// This analyzer restores the deterministic CCHTTP002 signal and rejects the retired LegacyStatic
/// registration mode even when the toolchain is present.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AppServiceHttpProjectionDiagnosticAnalyzer : DiagnosticAnalyzer
{
    private const string ProjectionAttributeName =
        "CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjectionAttribute";

    private const string ToolchainProperty = "build_property.CrestCreatesAppServiceHttpJsonToolchain";

    private static readonly DiagnosticDescriptor NotMaterialized = new(
        id: "CCHTTP002",
        title: "AppService HTTP projection is not materialized",
        messageFormat:
            "'{0}' declares a formal AppService HTTP projection, but the CrestCreates AppService HTTP/JSON "
            + "toolchain is not enabled; no endpoint will be published. Import the JsonContracts Build "
            + "props/targets or enable the AppService HTTP precompile toolchain.",
        category: "CrestCreates.HttpProjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RetiredRegistrationMode = new(
        id: "CCHTTP001",
        title: "AppService HTTP registration mode is retired",
        messageFormat:
            "'{0}' declares RegistrationMode.LegacyStatic, which is retired; use "
            + "HttpProjectionRegistrationMode.ExplicitHost.",
        category: "CrestCreates.HttpProjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingFormalDeclaration = new(
        id: "CCHTTP001",
        title: "AppService HTTP projection declaration is incomplete",
        messageFormat:
            "'{0}' declares an implicit legacy-static projection; the mode is retired. Declare "
            + "ContractType and select HttpProjectionRegistrationMode.ExplicitHost.",
        category: "CrestCreates.HttpProjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MethodLevelProjectionRetired = new(
        id: "CCHTTP001",
        title: "Method-level AppService HTTP projection is retired",
        messageFormat:
            "'{0}' declares [CapabilityCompatibilityProjection] on a method; method-level projection "
            + "belonged to the retired legacy front-end. Declare a class-level projection with "
            + "ContractType and HttpProjectionRegistrationMode.ExplicitHost.",
        category: "CrestCreates.HttpProjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            NotMaterialized,
            RetiredRegistrationMode,
            MissingFormalDeclaration,
            MethodLevelProjectionRetired);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var projectionAttribute = start.Compilation.GetTypeByMetadataName(ProjectionAttributeName);
            if (projectionAttribute is null)
            {
                return;
            }

            var toolchainEnabled = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                .TryGetValue(ToolchainProperty, out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

            start.RegisterSymbolAction(
                symbol => Analyze(symbol, projectionAttribute, toolchainEnabled),
                SymbolKind.NamedType);
            start.RegisterSymbolAction(
                symbol => AnalyzeMethod(symbol, projectionAttribute),
                SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(
        SymbolAnalysisContext context,
        INamedTypeSymbol projectionAttribute)
    {
        if (context.Symbol is not IMethodSymbol method)
        {
            return;
        }

        var attribute = method.GetAttributes().FirstOrDefault(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, projectionAttribute));
        if (attribute is null)
        {
            return;
        }

        var location = attribute.ApplicationSyntaxReference
                           ?.GetSyntax(context.CancellationToken)
                           .GetLocation()
                       ?? method.Locations.FirstOrDefault();
        if (location is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(MethodLevelProjectionRetired, location, method.Name));
        }
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol projectionAttribute,
        bool toolchainEnabled)
    {
        if (context.Symbol is not INamedTypeSymbol type)
        {
            return;
        }

        var attribute = type.GetAttributes().FirstOrDefault(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, projectionAttribute));
        if (attribute is null)
        {
            return;
        }

        var location = attribute.ApplicationSyntaxReference
                           ?.GetSyntax(context.CancellationToken)
                           .GetLocation()
                       ?? type.Locations.FirstOrDefault();
        if (location is null)
        {
            return;
        }

        var mode = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "RegistrationMode");
        var hasMode = mode.Key is not null && mode.Value.Kind == TypedConstantKind.Enum;
        var hasContract = attribute.NamedArguments.Any(argument =>
            argument.Key == "ContractType"
            && argument.Value.Kind == TypedConstantKind.Type
            && argument.Value.Value is not null);
        var legacyStatic = hasMode && Convert.ToInt32(mode.Value.Value) == 1;
        if (legacyStatic)
        {
            context.ReportDiagnostic(Diagnostic.Create(RetiredRegistrationMode, location, type.Name));
            return;
        }

        if (!hasMode && !hasContract)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingFormalDeclaration, location, type.Name));
            return;
        }

        if (!toolchainEnabled)
        {
            context.ReportDiagnostic(Diagnostic.Create(NotMaterialized, location, type.Name));
        }
    }
}
