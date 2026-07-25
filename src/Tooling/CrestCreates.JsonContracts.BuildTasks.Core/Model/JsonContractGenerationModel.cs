using CrestCreates.JsonContracts.BuildTasks.Diagnostics;

namespace CrestCreates.JsonContracts.BuildTasks.Model;

public sealed class JsonContractGenerationModel
{
    public List<JsonContractContextModel> Contexts { get; init; } = [];
    public List<JsonContractDiagnostic> Diagnostics { get; init; } = [];
    public bool HasErrors => Diagnostics.Any(d => d.Severity == JsonContractDiagnosticSeverity.Error);
}
