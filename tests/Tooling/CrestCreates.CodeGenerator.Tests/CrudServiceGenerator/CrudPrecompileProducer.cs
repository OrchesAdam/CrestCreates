using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.Tooling.CrudGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>The production CLI boundary materializes both stages before one compiler driver runs.</summary>
internal static class CrudPrecompileProducer
{
    internal sealed record Output(
        Compilation Compilation,
        IReadOnlyCollection<SyntaxTree> BundleTrees,
        IReadOnlyList<CrudGenerationModel> Models,
        IReadOnlyList<CrudGenerationDiagnostic> Diagnostics,
        IReadOnlyList<string> BundleCompileErrors);

    public static Output Run(string authoredSource, IReadOnlyList<MetadataReference> references,
        string assemblyName = "Shop.Application")
    {
        var directory = Path.Combine(Path.GetTempPath(), "crest-crud-driver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var parse = new CSharpParseOptions(LanguageVersion.Latest);
            var authoredPath = Path.Combine(directory, "Declaration.cs");
            var bundlePath = Path.Combine(directory, "CrestCreates.Crud.g.cs");
            var jsonPath = Path.Combine(directory, "CrestCreates.JsonContracts.g.cs");
            var sources = new List<object> { new { Path = authoredPath, Text = authoredSource } };
            var referencePaths = references.Select(r => r.Display!).ToArray();
            var prepare = Invoke("prepare-crud", directory, new
            {
                AssemblyName = assemblyName, Sources = sources, ReferencePaths = referencePaths,
                LangVersion = "latest", Nullable = "enable", DefineConstants = Array.Empty<string>(),
                ExpectedModelSchemaVersion = CrudGenerationModel.ModelSchemaVersion,
            });
            var bundleSource = System.Text.Encoding.UTF8.GetString(prepare.GetProperty("BundleBytes").GetBytesFromBase64());
            sources.Add(new { Path = bundlePath, Text = bundleSource });
            var json = Invoke("generate", directory, new
            {
                AssemblyName = assemblyName, Sources = sources, ReferencePaths = referencePaths,
                LangVersion = "latest", Nullable = "enable", DefineConstants = Array.Empty<string>(),
                ManifestAccessibility = "Internal", CrudOutputsManifestJson = prepare.GetProperty("Outputs").GetRawText(),
            });
            var jsonSource = System.Text.Encoding.UTF8.GetString(json.GetProperty("GeneratedSourceBytes").GetBytesFromBase64());
            var authored = CSharpSyntaxTree.ParseText(authoredSource, parse, authoredPath);
            var bundle = CSharpSyntaxTree.ParseText(bundleSource, parse, bundlePath);
            var context = CSharpSyntaxTree.ParseText(jsonSource, parse, jsonPath);
            var input = CSharpCompilation.Create(assemblyName, [authored, bundle, context], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var models = CrudGenerationModelBuilder.Build(
                CSharpCompilation.Create(assemblyName, [authored], references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)), assemblyName);

            // Both generators consume ordinary input at once. Never feed one driver's output to another.
            var stjPath = Directory.EnumerateFiles(Path.Combine(DotNetRoot(), "packs", "Microsoft.NETCore.App.Ref"),
                    "System.Text.Json.SourceGeneration.dll", SearchOption.AllDirectories)
                .OrderByDescending(p => p, StringComparer.Ordinal).First();
            var stj = new AnalyzerFileReference(stjPath, new Loader()).GetGenerators(LanguageNames.CSharp);
            if (stj.Length == 0) throw new InvalidOperationException("The official STJ generator did not load.");
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                stj.Add(new ObjectMappingSourceGenerator().AsSourceGenerator()), parseOptions: parse);
            driver = driver.RunGeneratorsAndUpdateCompilation(input, out var compilation, out var diagnostics);
            var run = driver.GetRunResult();
            if (run.Results.Any(r => r.Exception is not null)
                || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new InvalidOperationException("Compiler generators failed: " + string.Join(" | ", diagnostics));
            var producerTrees = new[] { bundle, context }.Concat(run.GeneratedTrees).ToArray();
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()).ToArray();
            return new Output(compilation, producerTrees, models.Models, models.Diagnostics, errors);
        }
        finally
        {
            // Only the disposable fixture directory is removed; application/repository files are untouched.
            Directory.Delete(directory, true);
        }
    }

    internal static JsonElement Invoke(string command, string directory, object request)
    {
        var requestPath = Path.Combine(directory, command + ".request.json");
        var responsePath = Path.Combine(directory, command + ".response.json");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(Path.Combine(RepositoryRoot(), "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks.Core",
            "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", "CrestCreates.JsonContracts.Tool.dll"));
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(requestPath);
        start.ArgumentList.Add(responsePath);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (!File.Exists(responsePath)) throw new InvalidOperationException(command + " produced no response: " + output);
        using var document = JsonDocument.Parse(File.ReadAllText(responsePath));
        if (process.ExitCode != 0) throw new InvalidOperationException(command + " failed: " + document.RootElement + output);
        return document.RootElement.Clone();
    }

    private static string DotNetRoot() => Directory.GetParent(typeof(object).Assembly.Location)!.Parent!.Parent!.Parent!.FullName;
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CrestCreates.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
    private sealed class Loader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { }
        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }

    public static CrudFormalContractProbe.Model ContractModel(CrudGenerationModel model) =>
        new(model.ServiceClassName, model.EntityClassName, model.ServiceNamespace);
}
