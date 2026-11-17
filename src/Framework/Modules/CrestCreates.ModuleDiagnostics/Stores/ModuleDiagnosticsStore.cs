using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.ModuleDiagnostics.Stores;

public class ModuleDiagnosticsStore : IModuleDiagnosticsStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<ModulePhaseDiagnostic>> _records = new(System.StringComparer.Ordinal);
    private int _totalCount;
    private int _failureCount;

    public bool HasFailures { get { lock (_sync) return _failureCount > 0; } }
    public int TotalCount { get { lock (_sync) return _totalCount; } }

    public void Record(ModulePhaseDiagnostic diagnostic)
    {
        lock (_sync)
        {
            if (!_records.TryGetValue(diagnostic.ModuleName, out var records))
                _records.Add(diagnostic.ModuleName, records = new List<ModulePhaseDiagnostic>());
            records.Add(diagnostic);
            _totalCount++;
            if (diagnostic.Status == ModulePhaseStatus.Failed) _failureCount++;
        }
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetAll()
    {
        lock (_sync) return _records.Values.SelectMany(records => records).ToArray();
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetByModule(string moduleName)
    {
        lock (_sync) return _records.TryGetValue(moduleName, out var records) ? records.ToArray() : System.Array.Empty<ModulePhaseDiagnostic>();
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetFailed()
    {
        lock (_sync) return _records.Values.SelectMany(records => records).Where(record => record.Status == ModulePhaseStatus.Failed).ToArray();
    }
}
