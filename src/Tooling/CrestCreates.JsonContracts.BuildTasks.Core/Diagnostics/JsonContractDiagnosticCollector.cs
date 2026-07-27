using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Diagnostics;

internal sealed class JsonContractDiagnosticCollector
{
    private readonly List<JsonContractDiagnostic> _diagnostics = [];

    public IReadOnlyList<JsonContractDiagnostic> Diagnostics => _diagnostics;

    public bool HasErrors { get; private set; }

    public void Report(JsonContractDiagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
        if (diagnostic.Severity == JsonContractDiagnosticSeverity.Error)
        {
            HasErrors = true;
        }
    }
}
