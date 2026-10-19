using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using CrestCreates.Tooling.HttpProjection;
using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.HttpProjection;

/// <summary>
/// #129 S2.2-B: isolated precompile producer for hand-written AppService HTTP projections. It reads the
/// authored sources plus the real TFM reference assemblies, builds the frozen projection model from
/// Roslyn symbols, and returns ordinary Compile artifacts plus a per-artifact manifest. Declarations
/// that do not name a ContractType stay with the legacy projection front-end and are not touched.
/// </summary>
public sealed class AppServiceHttpPrecompileEngine
{
    public const string ArtifactSchemaVersion = "appservice-http-artifacts-v1";

    private const string ProjectionAttributeName =
        "CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjectionAttribute";

    public AppServiceHttpPrecompileResult Run(
        string assemblyName,
        IEnumerable<(string Path, string Text)> sourceFiles,
        IReadOnlyList<string> referencePaths,
        string languageVersion,
        IEnumerable<string> defineConstants,
        string nullable,
        bool allowUnsafeBlocks,
        bool validateOnly = false)
    {
        var diagnostics = new List<AppServiceHttpToolDiagnostic>();
        if (referencePaths is null || referencePaths.Count == 0)
        {
            diagnostics.Add(new AppServiceHttpToolDiagnostic(
                AppServiceHttpDiagnosticCodes.NotMaterialized,
                "The AppService HTTP projection precompile received no reference assemblies.",
                null, 0, 0));
            return AppServiceHttpPrecompileResult.Failed(diagnostics);
        }

        var compilation = JsonContractCompilationFactory.CreateCompilation(
            assemblyName,
            sourceFiles.ToList(),
            referencePaths,
            languageVersion,
            defineConstants,
            nullable,
            allowUnsafeBlocks);

        var models = new List<AppServiceHttpProjectionModel>();
        foreach (var type in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type).OfType<INamedTypeSymbol>())
        {
            var projection = type.GetAttributes().FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString() == ProjectionAttributeName);
            if (projection is null || !HasContractType(projection))
            {
                // No ContractType: this is a legacy static declaration owned by the compatibility
                // front-end (or a plain [CrestService]); the formal HTTP stage does not engage.
                continue;
            }

            var build = AppServiceHttpProjectionModelBuilder.Build(compilation, type);
            foreach (var diagnostic in build.Diagnostics)
            {
                diagnostics.Add(new AppServiceHttpToolDiagnostic(
                    diagnostic.Id,
                    diagnostic.Message,
                    null,
                    0,
                    0,
                    diagnostic.Severity == AppServiceHttpDiagnosticSeverity.Error));
            }

            if (build.Model is not null)
            {
                models.Add(build.Model);
            }
        }

        if (validateOnly)
        {
            foreach (var model in models)
            {
                diagnostics.Add(new AppServiceHttpToolDiagnostic(
                    AppServiceHttpDiagnosticCodes.NotMaterialized,
                    $"HTTP projection generation is disabled, but '{model.ContractFullName}' declares an explicit-host projection.",
                    null,
                    0,
                    0));
            }

            return AppServiceHttpPrecompileResult.Failed(diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return AppServiceHttpPrecompileResult.Failed(diagnostics);
        }

        if (models.Count == 0)
        {
            return new AppServiceHttpPrecompileResult(diagnostics, Array.Empty<(string, string)>(), null, 0);
        }

        // The producer must compile under the real owner assembly name: capability ids, endpoint ids and
        // composed envelope type names are derived from it, so a synthetic name would leak into the wire
        // identities the host later maps (S2.2-B probe).
        models = models
            .OrderBy(model => model.ContractFullName, StringComparer.Ordinal)
            .ToList();
        var assemblyOptions = AppServiceHttpAssemblyOptions.ForAssembly(assemblyName);
        var optionsByModel = models.ToDictionary(
            model => model.ContractFullName,
            model => AppServiceHttpEmissionOptions.ForContract(model.ContractFullName) with
            {
                JsonContextFullName = assemblyOptions.JsonContextFullName,
            },
            StringComparer.Ordinal);

        var artifacts = new List<(string FileName, string Text)>();
        var entries = new List<AppServiceHttpOutputManifestEntry>();
        foreach (var model in models)
        {
            var bundle = AppServiceHttpProjectionEmitter.Emit(model, optionsByModel[model.ContractFullName]).Single();
            artifacts.Add((bundle.FileName, bundle.Source));
            entries.Add(ToEntry(
                AppServiceHttpProjectionEmitter.BuildManifestEntry(model, optionsByModel[model.ContractFullName]),
                RequiredRootNames(model, optionsByModel[model.ContractFullName])));
        }

        var activation = AppServiceHttpProjectionEmitter.EmitAssemblyActivation(
            assemblyName,
            models.Select(model => (model, optionsByModel[model.ContractFullName])).ToArray(),
            assemblyOptions);
        artifacts.Add((activation.FileName, activation.Source));
        entries.Add(new AppServiceHttpOutputManifestEntry
        {
            OwnerAssemblyName = assemblyName,
            OwnerServiceFullName = assemblyName,
            ContractAssemblyName = assemblyName,
            ArtifactHintName = activation.FileName,
            DeclaredTypeFullNames = HttpProjectionShared.DeclaredTypeFullNames(activation.Source).ToArray(),
            ReferencedTypeFullNames = Array.Empty<string>(),
            SourceLocation = models[0].DeclarationLocation,
            ModelSchemaVersion = AppServiceHttpProjectionModel.ModelSchemaVersion,
            ContentHash = ContentHashOf(activation.Source),
            RequiredJsonRootTypeNames = Array.Empty<string>(),
        });

        var manifest = new AppServiceHttpOutputManifestDocument
        {
            SchemaVersion = ArtifactSchemaVersion,
            ModelSchemaVersion = AppServiceHttpProjectionModel.ModelSchemaVersion,
            Entries = entries,
        };
        return new AppServiceHttpPrecompileResult(diagnostics, artifacts, manifest, models.Count);
    }

    private static string[] RequiredRootNames(
        AppServiceHttpProjectionModel model,
        AppServiceHttpEmissionOptions options)
        => AppServiceHttpProjectionEmitter.PlanJsonRoots(model, options)
            .Select(root => root.ClrTypeDisplay ?? root.TypeDisplay)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static string ContentHashOf(string source)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();

    private static bool HasContractType(AttributeData projection)
        => projection.NamedArguments.Any(argument =>
            argument.Key == "ContractType" && argument.Value.Kind == TypedConstantKind.Type && argument.Value.Value is not null);

    private static AppServiceHttpOutputManifestEntry ToEntry(
        AppServiceHttpManifestEntry entry,
        IReadOnlyList<string> requiredJsonRootTypeNames)
        => new()
        {
            RequiredJsonRootTypeNames = requiredJsonRootTypeNames.ToArray(),
            OwnerAssemblyName = entry.OwnerAssemblyName,
            OwnerServiceFullName = entry.OwnerServiceFullName,
            ContractAssemblyName = entry.ContractAssemblyName,
            ArtifactHintName = entry.ArtifactHintName,
            DeclaredTypeFullNames = entry.DeclaredTypeFullNames.ToArray(),
            ReferencedTypeFullNames = entry.ReferencedTypeFullNames.ToArray(),
            SourceLocation = entry.SourceLocation,
            ModelSchemaVersion = entry.ModelSchemaVersion,
            ContentHash = entry.ContentHash,
        };
}

public sealed record AppServiceHttpToolDiagnostic(
    string Id, string Message, string? Path, int Line, int Column, bool IsError = true);

public sealed record AppServiceHttpPrecompileResult(
    IReadOnlyList<AppServiceHttpToolDiagnostic> Diagnostics,
    IReadOnlyList<(string FileName, string Text)> Artifacts,
    AppServiceHttpOutputManifestDocument? Manifest,
    int DeclarationCount)
{
    public bool Succeeded => !Diagnostics.Any(diagnostic => diagnostic.IsError);

    public static AppServiceHttpPrecompileResult Failed(IReadOnlyList<AppServiceHttpToolDiagnostic> diagnostics)
        => new(diagnostics, Array.Empty<(string, string)>(), null, 0);
}

public sealed class AppServiceHttpOutputManifestDocument
{
    public string? SchemaVersion { get; set; }

    public string? ModelSchemaVersion { get; set; }

    public List<AppServiceHttpOutputManifestEntry>? Entries { get; set; }
}

public sealed class AppServiceHttpOutputManifestEntry
{
    public string? OwnerAssemblyName { get; set; }

    public string? OwnerServiceFullName { get; set; }

    public string? ContractAssemblyName { get; set; }

    public string? ArtifactHintName { get; set; }

    public string[]? DeclaredTypeFullNames { get; set; }

    public string[]? ReferencedTypeFullNames { get; set; }

    public string? SourceLocation { get; set; }

    public string? ModelSchemaVersion { get; set; }

    public string? ContentHash { get; set; }

    /// <summary>JSON roots the projection requires on the host's generated context.</summary>
    public string[]? RequiredJsonRootTypeNames { get; set; }
}
