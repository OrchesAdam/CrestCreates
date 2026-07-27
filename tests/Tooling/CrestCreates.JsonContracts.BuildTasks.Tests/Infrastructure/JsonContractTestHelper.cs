using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;

public static class JsonContractTestHelper
{
    public static JsonContractTestCompilation BuildModel(
        string assemblyName,
        IEnumerable<(string Path, string Text)> sourceFiles,
        IEnumerable<string>? referencePaths = null,
        bool allowUnsafeBlocks = false)
    {
        var compilationFactory = new JsonContractCompilationTestBase();
        var compilation = compilationFactory.CreateCompilation(
            assemblyName,
            sourceFiles,
            referencePaths,
            allowUnsafeBlocks: allowUnsafeBlocks);

        var builder = new JsonContractSurfaceModelBuilder();
        var model = builder.Build(compilation);

        return new JsonContractTestCompilation
        {
            Compilation = compilation,
            Model = model,
            Diagnostics = model.Diagnostics.ToList(),
        };
    }
}
