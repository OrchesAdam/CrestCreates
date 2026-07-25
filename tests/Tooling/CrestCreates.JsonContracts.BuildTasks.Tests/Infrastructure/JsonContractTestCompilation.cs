using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;

public sealed class JsonContractTestCompilation
{
    public CSharpCompilation? Compilation { get; init; }
    public JsonContractGenerationModel? Model { get; init; }
    public byte[]? GeneratedBytes { get; init; }
    public List<JsonContractDiagnostic> Diagnostics { get; init; } = [];
}
