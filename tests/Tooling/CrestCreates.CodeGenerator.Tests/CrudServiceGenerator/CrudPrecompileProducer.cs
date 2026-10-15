using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Tooling.CrudGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// #128 S2: test-side view of the CRUD precompile producer. It runs the same shared model builder and
/// emitters that the BuildTasks stage drives, adds the materialized surface as <b>ordinary</b> syntax
/// trees, and hands the result to <see cref="CrudFormalContractProbe"/>. The contract baselines
/// therefore assert against the real producer output instead of keeping the analyzer's
/// single-round GeneratedTrees as the thing under test.
/// </summary>
internal static class CrudPrecompileProducer
{
    internal sealed record Output(
        Compilation Compilation,
        IReadOnlyCollection<SyntaxTree> BundleTrees,
        IReadOnlyList<CrudGenerationModel> Models,
        IReadOnlyList<CrudGenerationDiagnostic> Diagnostics,
        IReadOnlyList<string> BundleCompileErrors);

    public static Output Run(
        string authoredSource,
        IReadOnlyList<MetadataReference> references,
        string assemblyName = "Shop.Application")
    {
        var authoredTree = CSharpSyntaxTree.ParseText(authoredSource);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [authoredTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var build = CrudGenerationModelBuilder.Build(compilation, assemblyName);

        List<CrudArtifact> artifacts = [];
        foreach (var model in build.Models)
        {
            artifacts.AddRange(CrudArtifactEmitter.Emit(model));
        }

        var bundleTrees = artifacts
            .Select(artifact => CSharpSyntaxTree.ParseText(artifact.Source, path: artifact.HintName))
            .ToArray();

        var updated = compilation.AddSyntaxTrees(bundleTrees);
        var bundleErrors = updated.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                                 && diagnostic.Location.SourceTree is { } tree
                                 && bundleTrees.Contains(tree))
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        return new Output(
            updated,
            bundleTrees,
            build.Models,
            build.Diagnostics,
            bundleErrors);
    }

    /// <summary>
    /// The model the S1 contract checker expects, read back from the producer rather than hardcoded,
    /// so a rename in the model breaks the baseline instead of silently passing.
    /// </summary>
    public static CrudFormalContractProbe.Model ContractModel(CrudGenerationModel model) =>
        new(model.ServiceClassName, model.EntityClassName, model.ServiceNamespace);
}
