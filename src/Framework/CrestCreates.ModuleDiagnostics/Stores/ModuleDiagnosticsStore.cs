using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.ModuleDiagnostics.Stores;

public class ModuleDiagnosticsStore : IModuleDiagnosticsStore
{
    private readonly ConcurrentDictionary<string, List<ModulePhaseDiagnostic>> _records = new();
    private volatile int _totalCount;
    private volatile int _failureCount;

    public bool HasFailures => _failureCount > 0;
    public int TotalCount => _totalCount;

    public void Record(ModulePhaseDiagnostic diagnostic)
    {
        _records.AddOrUpdate(
            diagnostic.ModuleName,
            _ => new List<ModulePhaseDiagnostic> { diagnostic },
            (_, list) =>
            {
                list.Add(diagnostic);
                return list;
            });

        System.Threading.Interlocked.Increment(ref _totalCount);

        if (diagnostic.Status == ModulePhaseStatus.Failed)
        {
            System.Threading.Interlocked.Increment(ref _failureCount);
        }
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetAll()
    {
        return _records.Values.SelectMany(v => v).ToList();
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetByModule(string moduleName)
    {
        if (_records.TryGetValue(moduleName, out var list))
        {
            return list.ToList();
        }
        return System.Array.Empty<ModulePhaseDiagnostic>();
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetFailed()
    {
        return GetAll().Where(d => d.Status == ModulePhaseStatus.Failed).ToList();
    }
}