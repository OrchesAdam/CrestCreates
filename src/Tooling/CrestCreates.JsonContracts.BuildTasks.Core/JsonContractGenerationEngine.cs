using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Generation;
using CrestCreates.JsonContracts.BuildTasks.Incremental;
using CrestCreates.JsonContracts.BuildTasks.Semantic;

namespace CrestCreates.JsonContracts.BuildTasks;

public sealed class JsonContractGenerationResult
{
    public required IReadOnlyList<JsonContractDiagnostic> Diagnostics { get; init; }
    public required int ContextCount { get; init; }
    public required string GeneratedSource { get; init; }
    public required int SurfaceRootCount { get; init; }
    public required int ExplicitRootCount { get; init; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == JsonContractDiagnosticSeverity.Error);
}

public sealed class JsonContractGenerationEngine
{
    public JsonContractGenerationResult Generate(
        string assemblyName,
        IReadOnlyList<(string Path, string Text)> sources,
        IReadOnlyList<string> referencePaths,
        string langVersion,
        IReadOnlyList<string> defineConstants,
        string nullable,
        bool allowUnsafeBlocks,
        string manifestAccessibility)
    {
        var compilation = JsonContractCompilationFactory.Create(
            assemblyName,
            sources,
            referencePaths,
            langVersion,
            defineConstants,
            nullable,
            allowUnsafeBlocks);

        var builder = new JsonContractSurfaceModelBuilder();
        var model = builder.Build(compilation);

        var accessibility = JsonContractSurfaceModelBuilder.ParseManifestAccessibility(manifestAccessibility);
        foreach (var ctx in model.Contexts)
            ctx.ManifestAccessibility = accessibility;

        var sourceText = JsonContractSourceWriter.WriteContextSource(model);

        return new JsonContractGenerationResult
        {
            Diagnostics = model.Diagnostics,
            ContextCount = model.Contexts.Count,
            GeneratedSource = sourceText,
            SurfaceRootCount = model.Contexts.Sum(c => c.SurfaceRoots.Count),
            ExplicitRootCount = model.Contexts.Sum(c => c.ExplicitRoots.Count)
        };
    }
}
