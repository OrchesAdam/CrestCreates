using System;

namespace CrestCreates.ModuleDiagnostics.Stores;

public enum ModulePhaseStatus
{
    Success,
    Failed
}

public readonly struct ModulePhaseDiagnostic
{
    public string ModuleName { get; }
    public string Phase { get; }
    public ModulePhaseStatus Status { get; }
    public TimeSpan Elapsed { get; }
    public string? ErrorMessage { get; }

    public ModulePhaseDiagnostic(string moduleName, string phase, ModulePhaseStatus status, TimeSpan elapsed, string? errorMessage)
    {
        ModuleName = moduleName;
        Phase = phase;
        Status = status;
        Elapsed = elapsed;
        ErrorMessage = errorMessage;
    }
}