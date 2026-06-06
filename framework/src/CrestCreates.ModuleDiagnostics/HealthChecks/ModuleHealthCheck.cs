using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.ModuleDiagnostics.Stores;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.ModuleDiagnostics.HealthChecks;

public class ModuleHealthCheck : IHealthCheck
{
    private readonly IModuleDiagnosticsStore _store;

    public ModuleHealthCheck(IModuleDiagnosticsStore store)
    {
        _store = store;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var allEntries = _store.GetAll();
        var failedEntries = _store.GetFailed();

        var moduleNames = allEntries.Select(e => e.ModuleName).Distinct().ToList();
        var failedModuleNames = failedEntries.Select(e => e.ModuleName).Distinct().ToList();

        // Build per-module phase details
        var modules = allEntries
            .GroupBy(e => e.ModuleName)
            .OrderBy(g => g.Key)
            .Select(g => new Dictionary<string, object>
            {
                ["name"] = g.Key,
                ["status"] = g.Any(e => e.Status == ModulePhaseStatus.Failed) ? "Failed" : "Success",
                ["phases"] = g.Select(e => new Dictionary<string, object>
                {
                    ["phase"] = e.Phase,
                    ["status"] = e.Status == ModulePhaseStatus.Success ? "Success" : "Failed",
                    ["elapsedMs"] = e.Elapsed.TotalMilliseconds,
                    ["error"] = e.ErrorMessage ?? ""
                }).ToList<object>()
            }).ToList<object>();

        var data = new Dictionary<string, object>
        {
            { "totalModules", moduleNames.Count },
            { "failedModules", failedModuleNames.Count },
            { "totalPhases", allEntries.Count },
            { "failedPhases", failedEntries.Count },
            { "modules", modules }
        };

        if (failedEntries.Count > 0)
        {
            var failedDetails = failedEntries.Select(e => new Dictionary<string, string>
            {
                { "module", e.ModuleName },
                { "phase", e.Phase },
                { "error", e.ErrorMessage ?? "Unknown error" }
            }).ToList<object>();

            data["failedDetails"] = failedDetails;

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Modules: {failedModuleNames.Count} failed, {failedEntries.Count} phase(s) failed",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Modules: all {moduleNames.Count} modules healthy",
            data));
    }
}
