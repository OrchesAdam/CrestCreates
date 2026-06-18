using System.Collections.Generic;

namespace CrestCreates.ModuleDiagnostics.Stores;

public interface IModuleDiagnosticsStore
{
    void Record(ModulePhaseDiagnostic diagnostic);
    IReadOnlyList<ModulePhaseDiagnostic> GetAll();
    IReadOnlyList<ModulePhaseDiagnostic> GetByModule(string moduleName);
    IReadOnlyList<ModulePhaseDiagnostic> GetFailed();
    bool HasFailures { get; }
    int TotalCount { get; }
}