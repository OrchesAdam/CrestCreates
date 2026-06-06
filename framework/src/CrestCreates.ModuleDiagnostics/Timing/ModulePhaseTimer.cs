using System;
using System.Diagnostics;
using CrestCreates.ModuleDiagnostics.Stores;

namespace CrestCreates.ModuleDiagnostics.Timing;

public readonly struct ModulePhaseTimer
{
    private readonly string _moduleName;
    private readonly string _phase;
    private readonly long _startTimestamp;

    private ModulePhaseTimer(string moduleName, string phase, long startTimestamp)
    {
        _moduleName = moduleName;
        _phase = phase;
        _startTimestamp = startTimestamp;
    }

    public static ModulePhaseTimer StartNew(string moduleName, string phase)
    {
        return new ModulePhaseTimer(moduleName, phase, Stopwatch.GetTimestamp());
    }

    public ModulePhaseDiagnostic Stop(ModulePhaseStatus status)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        return new ModulePhaseDiagnostic(_moduleName, _phase, status, elapsed, null);
    }

    public ModulePhaseDiagnostic StopFailed(Exception ex)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        return new ModulePhaseDiagnostic(_moduleName, _phase, ModulePhaseStatus.Failed, elapsed, ex.Message);
    }
}